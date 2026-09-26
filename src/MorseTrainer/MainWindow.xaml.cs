using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer;

public partial class MainWindow : Window
{
    private static readonly HttpClient UpdateClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    // Установщик весит десятки мегабайт: отдельный клиент с длинным таймаутом
    private static readonly HttpClient DownloadClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly SettingsService _settingsService = new();
    private readonly TrainingHistoryStore _historyStore = new(AppPaths.HistoryFile);
    private readonly ChantStore _chantStore = new(AppPaths.ChantsFile);
    private DispatcherTimer? _reminderTimer;
    private TrayReminder? _trayReminder;
    private DateOnly? _reminderShownOn;
    private int _courseStep;
    private IReadOnlyList<TrainingRecord> _history = Array.Empty<TrainingRecord>();
    private bool _currentTaskRecorded;
    private DateTime _currentTaskStartedAt = DateTime.Now;
    private ExamSession? _exam;
    private DrillPlan? _currentDrill;
    private DispatcherTimer? _examTimer;
    private string? _examReport;
    private string _autoSpeedNote = string.Empty;
    private const int QuizTabIndex = 2;
    private const int KeyerTabIndex = 3;
    private readonly Stopwatch _keyerClock = Stopwatch.StartNew();
    private KeyerDecoder? _keyer;
    private int _keyerSpeed;
    private int _keyerAlphabet = -1;
    private bool _keyDown;
    private double _keyerPressStart;
    private double _keyerLastRelease = -1;
    private SoundPlayer? _tonePlayer;
    private MemoryStream? _toneStream;
    private int _toneFrequency;
    private int _toneVolume;
    private DispatcherTimer? _keyerTimer;
    private string _keyerTarget = string.Empty;
    private readonly ProfileService _profileService = new();
    private readonly Dictionary<char, int> _problemSymbols = new();
    private SoundPlayer? _player;
    private MemoryStream? _audioStream;
    private CancellationTokenSource? _playbackCancellation;
    private SoundPlayer? _learningPlayer;
    private MemoryStream? _learningAudioStream;
    private CancellationTokenSource? _learningCancellation;
    private string _currentTask = string.Empty;
    private string _selectedSymbols = "АБВГДЕЖЗИКЛМНОПРСТУ";
    private AudioClip? _currentClip;
    private AppSettings? _currentSettings;
    private IReadOnlyList<LearningSymbolItem> _visibleLearningItems = Array.Empty<LearningSymbolItem>();
    private LearningSymbolItem? _quizTarget;
    private bool _answerVisible;
    private int _playingGroup;   // какая группа звучит сейчас (с 1); 0 — не звучит или идёт сигнал Ж Ж Ж
    private bool _windowLoaded;
    private bool _applyingProfile;
    private IReadOnlyList<TrainingProfile> _profiles = Array.Empty<TrainingProfile>();
    private int _attempts;
    private int _quizCorrect;
    private int _quizTotal;
    private int _quizAlphabet;
    private int _quizSpeed = EarQuiz.DefaultSpeed;
    private int _learningSpeed = EarQuiz.DefaultSpeed;
    private bool _quizAnswered = true;
    private Dictionary<string, int> _quizMisses = new();   // «На слух»: сколько раз путали символ — такие звучат чаще
    private CancellationTokenSource? _quizNextCancellation;
    private int _learningAlphabet;
    private int _learningAudioMode = 1;
    private bool _answerInputPreferred;
    private DateTime? _lastPracticeAt;
    private List<string> _practiceDays = new();
    private static readonly Brush QuizMarkTextBrush = CreateFrozenBrush("#10251D");

    public MainWindow()
    {
        InitializeComponent();
        // Варианты лимита экзамена переводятся кодом: до ApplySettings, который выбирает сохранённый
        ExamLimitCombo.ItemsSource = ExamSession.TimeLimitChoices
            .Select(minutes => minutes == 0 ? Texts.T("без лимита") : Texts.F("{0} мин", minutes))
            .ToArray();
        ReminderTimeCombo.ItemsSource = PracticeNudge.TimeChoices
            .Select(minutes => ReminderSchedule.ToTime(minutes).ToString(@"hh\:mm", CultureInfo.InvariantCulture))
            .ToArray();
        DailyGoalCombo.ItemsSource = TrainingStatistics.DailyGoalChoices
            .Select(minutes => minutes == 0 ? Texts.T("без цели") : Texts.F("{0} мин", minutes))
            .ToArray();
        ApplyWindowBounds(_settingsService.Load());
    }

    /// <summary>Размер окна и вкладка из прошлого запуска; применяется до показа, чтобы окно сразу встало по центру нужного размера.</summary>
    private void ApplyWindowBounds(AppSettings settings)
    {
        // Маленький экран (1366×768 и меньше): окно и его минимум не выходят за рабочую область
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        SetTrainingPanelCollapsed(settings.TrainingPanelCollapsed);
        SetAnswerInput(settings.ShowAnswerInput, remember: true);

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        // Спрятанная вкладка («Прогресс» без ввода ответа) не открывается — вместо неё «Тренировка»
        var tab = Math.Clamp(settings.MainTabIndex, 0, MainTabs.Items.Count - 1);
        MainTabs.SelectedIndex = ((TabItem)MainTabs.Items[tab]).Visibility == Visibility.Visible ? tab : 0;
    }

    private static Brush CreateFrozenBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private void SaveSettingsIfLoaded()
    {
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        SubtitleText.Text = Texts.F("Тренировка и изучение азбуки Морзе · версия {0}", CurrentVersion);
        var settings = _settingsService.Load();
        ApplySettings(settings);
        LoadProfiles(settings);
        ThemeService.Apply((AppTheme)Math.Clamp(settings.ThemeIndex, 0, 2));
        _quizCorrect = settings.QuizCorrect;
        _quizTotal = settings.QuizTotal;
        UpdateQuizScore();
        UpdateSettingLabels();
        UpdateCustomSymbolsVisibility();
        UpdateSelectedSymbolsSummary();
        LearningCatalog.SetCustomChants(_chantStore.Load());
        UpdateCustomVoiceText();
        RefreshLearningItems();
        _history = _historyStore.Load();
        RefreshProgress();
        RefreshCourse();
        ShowNudgeBanner();
        StartReminderTimer();
        _windowLoaded = true;
        await GenerateTaskAsync();
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        CancelQuizNext();
        _examTimer?.Stop();
        _reminderTimer?.Stop();
        _trayReminder?.Dispose();
        _trayReminder = null;
        StopPlayback(resetProgress: false);
        StopLearningPlayback();
        StopKeyerTone();
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    private async void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.G && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            MainTabs.SelectedIndex = 0;
            await GenerateTaskAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.E && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await StartExamAsync();
        }
        else if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            MainTabs.SelectedIndex = KeyerTabIndex;
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && MainTabs.SelectedIndex == 0)
        {
            await PlayCurrentAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && MainTabs.SelectedIndex == KeyerTabIndex)
        {
            if (!e.IsRepeat)
            {
                StartKeyPress();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            StopPlayback();
            StopLearningPlayback();
            e.Handled = true;
        }
    }

    // «На слух» с клавиатуры: пробел — ещё раз, Enter — новый символ. Перехват до кнопок: иначе пробел нажал бы кнопку в фокусе
    private async void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MainTabs.SelectedIndex != QuizTabIndex || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox
            || (e.Key != Key.Space && e.Key != Key.Enter))
        {
            return;
        }

        e.Handled = true;
        if (e.IsRepeat)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            await RepeatQuizAsync();
        }
        else
        {
            await AskQuizAsync();
        }
    }

    // «На слух»: набранный символ — ответ (в любой раскладке: латинская W засчитывается как В с тем же кодом)
    private void Window_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (MainTabs.SelectedIndex != QuizTabIndex || e.OriginalSource is TextBox || string.IsNullOrEmpty(e.Text) || char.IsWhiteSpace(e.Text[0]))
        {
            return;
        }

        if (FindQuizAnswerButton(e.Text[0]) is { } button)
        {
            e.Handled = true;
            AnswerQuiz(button);
        }
    }

    private void Window_OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _keyDown)
        {
            EndKeyPress();
            e.Handled = true;
        }
    }

    private static Version CurrentVersion =>
        UpdateService.Normalize(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // Единственное место, где приложение выходит в интернет, и только по нажатию кнопки
    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = Texts.T("Проверяю…");
        try
        {
            var info = await UpdateService.FetchLatestAsync(UpdateClient, CancellationToken.None);
            if (UpdateService.IsNewer(CurrentVersion, info.LatestVersion))
            {
                var notes = string.IsNullOrWhiteSpace(info.Notes) ? string.Empty : "\n\n" + Truncate(info.Notes, 600);
                if (info.WindowsInstallerUrl is null)
                {
                    var answer = MessageBox.Show(this,
                        Texts.F("Доступна версия {0}, у вас {1}.{2}\n\nОткрыть страницу загрузки?", info.LatestVersion, CurrentVersion, notes),
                        Texts.T("Обновление Morse Trainer"), MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (answer == MessageBoxResult.Yes)
                    {
                        OpenInBrowser(info.ReleasePageUrl);
                    }
                }
                else
                {
                    // Да — скачать установщик и запустить обновление, Нет — открыть страницу, Отмена — позже
                    var answer = MessageBox.Show(this,
                        Texts.F("Доступна версия {0}, у вас {1}.{2}\n\nДа — скачать установщик и обновиться сейчас, Нет — открыть страницу загрузки.", info.LatestVersion, CurrentVersion, notes),
                        Texts.T("Обновление Morse Trainer"), MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
                    if (answer == MessageBoxResult.Yes)
                    {
                        await DownloadAndRunInstallerAsync(info.WindowsInstallerUrl);
                    }
                    else if (answer == MessageBoxResult.No)
                    {
                        OpenInBrowser(info.ReleasePageUrl);
                    }
                }
            }
            else
            {
                MessageBox.Show(this, Texts.F("У вас последняя версия {0}.", CurrentVersion), Texts.T("Обновление Morse Trainer"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                Texts.F("Не удалось проверить обновления. Проверьте подключение к интернету.\n\n{0}", exception.Message),
                Texts.T("Обновление Morse Trainer"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CheckUpdatesButton.Content = Texts.T("Обновления");
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static void OpenInBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Скачивает установщик во временную папку с прогрессом на кнопке, запускает его и закрывает программу.</summary>
    private async Task DownloadAndRunInstallerAsync(string url)
    {
        var path = Path.Combine(Path.GetTempPath(), "MorseTrainer-Setup-x64.exe");
        using var response = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(path))
        {
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                CheckUpdatesButton.Content = total > 0
                    ? Texts.F("Скачиваю… {0}%", received * 100 / total.Value)
                    : Texts.F("Скачиваю… {0} МБ", received / (1024 * 1024));
            }
        }

        CheckUpdatesButton.Content = Texts.T("Запускаю установщик…");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private void LanguageCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowLoaded || LanguageCombo is null)
        {
            return;
        }

        _settingsService.Save(ReadSettings());
        MessageBox.Show(this, Texts.T("Язык интерфейса изменится после перезапуска приложения."), "Morse Trainer",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ThemeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo is null)
        {
            return;
        }

        ThemeService.Apply((AppTheme)Math.Clamp(ThemeCombo.SelectedIndex, 0, 2));
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    // ---------- Кнопки выбора вместо выпадающих списков (как на телефоне) ----------
    /// <summary>Выбранная кнопка — мятная (PrimaryButton), остальные в обычном стиле.</summary>
    private void HighlightChoice(IReadOnlyList<Button> buttons, int selected)
    {
        for (var index = 0; index < buttons.Count; index++)
        {
            buttons[index].Style = (Style)FindResource(index == selected ? "PrimaryButton" : typeof(Button));
        }
    }

    private static int ChoiceIndex(object sender, int max) =>
        sender is Button { Tag: string tag } && int.TryParse(tag, out var index) ? Math.Clamp(index, 0, max) : -1;

    // Пасхалка: щелчок по значку «· — —» в шапке — «НЕВАДИМКА» азбукой на 175 знаков/мин
    private void Logo_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Пасхалка не перебивает прослушивание задания — на экзамене оно может быть единственным
        if (_playbackCancellation is not null)
        {
            return;
        }

        StopPlayback();
        StopLearningPlayback();
        try
        {
            var clip = MorseAudioService.Render(EasterEgg.Text, EasterEgg.Speed, (int)FrequencySlider.Value, (int)VolumeSlider.Value, 3, 7);
            _learningAudioStream = new MemoryStream(clip.WavBytes, writable: false);
            _learningPlayer = new SoundPlayer(_learningAudioStream);
            _learningPlayer.Load();
            _learningPlayer.Play();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            // Нет звука — пасхалка просто молчит
        }
    }

    private void SetStatus(string text, bool isActive)
    {
        StatusBadgeText.Text = text;
        StatusBadgeText.Foreground = (Brush)FindResource(isActive ? "PrimaryBrush" : "DangerBrush");
        StatusBadge.Background = (Brush)FindResource(isActive ? "PrimaryDarkBrush" : "CardAltBrush");
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{(int)duration.TotalMinutes}:{duration.Seconds:00}"
        : Texts.F("{0} сек", Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds)));

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength
        ? value
        : value[..maxLength] + "…";
}
