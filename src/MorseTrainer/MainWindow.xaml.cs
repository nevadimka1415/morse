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
    private bool _windowLoaded;
    private bool _applyingProfile;
    private IReadOnlyList<TrainingProfile> _profiles = Array.Empty<TrainingProfile>();
    private int _attempts;
    private int _quizCorrect;
    private int _quizTotal;
    private int _quizAlphabet;
    private int _quizSpeed = EarQuiz.DefaultSpeed;
    private bool _quizAnswered = true;
    private CancellationTokenSource? _quizNextCancellation;
    private int _learningAlphabet;
    private int _learningAudioMode = 1;
    private bool _answerInputPreferred;
    private DateTime? _lastPracticeAt;
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

    // ---------- Курс «С нуля до 60 зн/мин» ----------

    private IReadOnlyList<CourseStep> CourseSteps => Course.Steps(Course.AlphabetFor(AlphabetCombo.SelectedIndex));

    private void RefreshCourse()
    {
        var steps = CourseSteps;
        var step = Course.Find(steps, _courseStep);
        var passed = Course.PassedCount(_history, steps);
        if (step is null)
        {
            CourseTitleText.Text = Texts.T("Курс «С нуля до 60 зн/мин»");
            CourseDetailsText.Text = Texts.F("{0} шагов: метод Коха по 4 символа, слова на 50 и 60 зн/мин, итоговый экзамен. Кнопка ставит нужные настройки и создаёт задание.", steps.Count);
            CourseStatusText.Text = passed > 0 ? Texts.F("Пройдено шагов: {0} из {1}.", passed, steps.Count) : " ";
            CourseStatusText.Foreground = (Brush)FindResource("MutedTextBrush");
            CourseStartButton.Content = Texts.T("Начать курс");
            CourseNextButton.Visibility = Visibility.Collapsed;
            CourseResetButton.Visibility = Visibility.Collapsed;
            return;
        }

        var stepPassed = Course.IsPassed(_history, step);
        CourseTitleText.Text = Texts.F("Курс «С нуля до 60 зн/мин» · шаг {0} из {1}: {2}", step.Number, steps.Count, step.Title);
        CourseDetailsText.Text = step.Details;
        CourseStatusText.Text = Course.Status(_history, step) + " " + Texts.F("Пройдено шагов: {0} из {1}.", passed, steps.Count);
        CourseStatusText.Foreground = (Brush)FindResource(stepPassed ? "PrimaryBrush" : "MutedTextBrush");
        CourseStartButton.Content = step.IsExam ? Texts.T("Начать экзамен") : Texts.T("Начать шаг");
        CourseNextButton.Visibility = step.Number < steps.Count ? Visibility.Visible : Visibility.Collapsed;
        CourseNextButton.IsEnabled = stepPassed;
        CourseNextButton.ToolTip = stepPassed ? null : Texts.F("Откроется, когда в задании этого шага будет точность от {0} %.", Course.PassAccuracy);
        CourseResetButton.Visibility = Visibility.Visible;
    }

    private async void CourseStartButton_OnClick(object sender, RoutedEventArgs e)
    {
        _courseStep = Math.Max(1, _courseStep);
        await StartCourseStepAsync();
    }

    private async void CourseNextButton_OnClick(object sender, RoutedEventArgs e)
    {
        _courseStep = Math.Min(CourseSteps.Count, _courseStep + 1);
        await StartCourseStepAsync();
    }

    private void CourseResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _courseStep = 0;
        _settingsService.Save(ReadSettings());
        RefreshCourse();
    }

    /// <summary>Ставит настройки текущего шага, переходит на «Тренировку» и создаёт задание (или экзамен).</summary>
    private async Task StartCourseStepAsync()
    {
        if (Course.Find(CourseSteps, _courseStep) is not { } step)
        {
            return;
        }

        var settings = ReadSettings();
        Course.Apply(step, settings);
        ApplySettings(settings);
        UpdateSettingLabels();
        UpdateCustomSymbolsVisibility();
        _settingsService.Save(settings);
        RefreshCourse();
        MainTabs.SelectedIndex = 0;
        SetAnswerInput(true, remember: false);
        if (step.IsExam)
        {
            await StartExamAsync();
        }
        else
        {
            await GenerateTaskAsync();
        }
    }

    // ---------- Напоминание на Windows ----------

    private void ShowNudgeBanner()
    {
        var text = PracticeNudge.Banner(_history, DateOnly.FromDateTime(DateTime.Now), _lastPracticeAt);
        NudgeText.Text = text ?? " ";
        NudgeBanner.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NudgeStartButton_OnClick(object sender, RoutedEventArgs e)
    {
        NudgeBanner.Visibility = Visibility.Collapsed;
        MainTabs.SelectedIndex = 0;
        PlayButton.Focus();
    }

    private void NudgeCloseButton_OnClick(object sender, RoutedEventArgs e) => NudgeBanner.Visibility = Visibility.Collapsed;

    private void ReminderOption_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
            CheckReminder();
        }
    }

    /// <summary>Раз в минуту: пора ли показать уведомление о тренировке (только пока программа открыта).</summary>
    private void StartReminderTimer()
    {
        _reminderTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _reminderTimer.Tick -= ReminderTimer_OnTick;
        _reminderTimer.Tick += ReminderTimer_OnTick;
        _reminderTimer.Start();
    }

    private void ReminderTimer_OnTick(object? sender, EventArgs e) => CheckReminder();

    private void CheckReminder()
    {
        if (ReminderCheckBox.IsChecked != true)
        {
            return;
        }

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var trainedToday = PracticeNudge.PracticedOn(today, _history, _lastPracticeAt);
        if (!PracticeNudge.IsReminderDue(now, SelectedReminderMinutes, _reminderShownOn, trainedToday))
        {
            return;
        }

        _reminderShownOn = today;
        try
        {
            _trayReminder ??= new TrayReminder(this, ActivateFromReminder);
            _trayReminder.Show("Morse Trainer", Texts.T("Пора потренироваться: пять минут азбуки Морзе."));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            // Уведомления недоступны (например, нет оболочки Windows) — остаётся плашка при следующем запуске
        }
    }

    private int SelectedReminderMinutes => PracticeNudge.TimeChoices[Math.Clamp(ReminderTimeCombo.SelectedIndex, 0, PracticeNudge.TimeChoices.Count - 1)];

    private void ActivateFromReminder()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        MainTabs.SelectedIndex = 0;
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

    private bool _refreshingProfileFilter;

    /// <summary>История, видимая на вкладке «Прогресс»: вся или одного профиля из фильтра.</summary>
    private IReadOnlyList<TrainingRecord> VisibleHistory =>
        ProgressProfileCombo?.SelectedIndex > 0 && ProgressProfileCombo.SelectedItem is string name
            ? TrainingStatistics.ForProfile(_history, name)
            : _history;

    private void RefreshProfileFilter()
    {
        var items = new List<string> { Texts.T("Все профили") };
        items.AddRange(TrainingStatistics.ProfileNames(_history));
        var selected = ProgressProfileCombo.SelectedItem as string;
        _refreshingProfileFilter = true;
        ProgressProfileCombo.ItemsSource = items;
        var index = selected is null ? -1 : items.FindIndex(item => string.Equals(item, selected, StringComparison.OrdinalIgnoreCase));
        ProgressProfileCombo.SelectedIndex = index > 0 ? index : 0;
        _refreshingProfileFilter = false;
    }

    private void ProgressProfileCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingProfileFilter && _windowLoaded)
        {
            RefreshProgress();
        }
    }

    private void ExportCsvButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Экспорт CSV"),
            Filter = Texts.T("Таблица CSV (*.csv)|*.csv"),
            FileName = $"morse-history-{DateTime.Now:yyyy-MM-dd}.csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // BOM нужен, чтобы Excel распознал UTF-8 и кириллицу
            File.WriteAllText(dialog.FileName, TrainingStatistics.ToCsv(VisibleHistory), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Экспорт CSV"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Экспорт истории"),
            Filter = Texts.T("История Morse Trainer (*.json)|*.json"),
            FileName = $"morse-history-{DateTime.Now:yyyy-MM-dd}.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, HistoryTransfer.Export(_history), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Экспорт истории"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Texts.T("Импорт истории"),
            Filter = Texts.T("История Morse Trainer (*.json)|*.json|Все файлы (*.*)|*.*")
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = _historyStore.Merge(HistoryTransfer.Import(File.ReadAllText(dialog.FileName)));
            _history = result.History;
            RefreshProgress();
            MessageBox.Show(this, result.Describe(), Texts.T("Импорт истории"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Импорт истории"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshProgress()
    {
        RefreshProfileFilter();
        var history = VisibleHistory;
        var summary = TrainingStatistics.Summarize(history);
        ProgressSessionsText.Text = summary.Sessions.ToString(CultureInfo.InvariantCulture);
        ProgressAverageText.Text = summary.Sessions == 0 ? "—" : $"{summary.AverageAccuracy:0.#}%";
        ProgressBestText.Text = summary.Sessions == 0 ? "—" : $"{summary.BestAccuracy:0.#}%";
        ProgressSymbolsText.Text = summary.Sessions == 0 ? "—" : $"{summary.CorrectSymbols} / {summary.TotalSymbols}";
        var problems = TrainingStatistics.ProblemSymbols(history);
        HistoryProblemSymbolsText.Text = problems.Count == 0
            ? "—"
            : string.Join("  ", problems.Select(item => $"{item.Symbol} ×{item.Count}"));
        ExamSeriesText.Text = TrainingStatistics.Exams(history).Describe();
        HistoryListView.ItemsSource = history
            .OrderByDescending(item => item.CompletedAt)
            .Take(50)
            .Select(item => new HistoryRow(
                item.CompletedAt.ToString("dd.MM.yy HH:mm", CultureInfo.CurrentCulture),
                (item.IsExam ? Texts.F("{0} · экзамен", item.ProfileName) : item.ProfileName) +
                (item.CourseStep > 0 ? Texts.F(" · шаг {0}", item.CourseStep) : string.Empty),
                Texts.F("{0} зн/мин", item.CharactersPerMinute),
                item.GroupCount.ToString(CultureInfo.InvariantCulture),
                $"{item.AccuracyPercent:0.#}%",
                item.ProblemSymbols.Length == 0 ? "—" : string.Join(" ", item.ProblemSymbols.Distinct())))
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var goal = TrainingStatistics.Goal(history, SelectedDailyGoal, today);
        GoalText.Text = goal.Describe();
        GoalText.Foreground = (Brush)FindResource(goal.IsMet ? "PrimaryBrush" : "TextBrush");
        GoalProgress.Value = goal.Progress * 100;
        GoalProgress.Visibility = goal.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        StreakText.Text = TrainingStatistics.Streak(history, today).Describe();

        DailyBarsPanel.Children.Clear();
        var days = TrainingStatistics.ByDay(history);
        if (days.Count == 0)
        {
            DailyBarsPanel.Children.Add(new TextBlock { Text = Texts.T("Пока пусто"), Foreground = (Brush)FindResource("MutedTextBrush") });
        }

        foreach (var day in days)
        {
            var met = goal.IsEnabled && day.Minutes >= goal.GoalMinutes ? " ✓" : string.Empty;
            AddBarRow(DailyBarsPanel, day.Date, day.AverageAccuracy / 100,
                $"{day.AverageAccuracy:0.#}% · {day.Sessions} · {day.Minutes:0}{met}");
        }

        SpeedBarsPanel.Children.Clear();
        var speeds = TrainingStatistics.SpeedByDay(history);
        if (speeds.Count == 0)
        {
            SpeedBarsPanel.Children.Add(new TextBlock
            {
                Text = Texts.T("Пока нет заданий с точностью от 90 %"),
                Foreground = (Brush)FindResource("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        // Полосы скорости — относительно лучшего дня в окне, чтобы рост был виден и на малых скоростях
        var fastest = speeds.Count == 0 ? 1 : speeds.Max(item => item.CharactersPerMinute);
        foreach (var speed in speeds)
        {
            AddBarRow(SpeedBarsPanel, speed.Date, (double)speed.CharactersPerMinute / fastest, Texts.F("{0} зн/мин", speed.CharactersPerMinute));
        }
    }

    private int SelectedDailyGoal => TrainingStatistics.DailyGoalChoices[Math.Clamp(DailyGoalCombo.SelectedIndex, 0, TrainingStatistics.DailyGoalChoices.Count - 1)];

    /// <summary>Строка полосы: дата, заполненная доля 0…1 и подпись справа.</summary>
    private void AddBarRow(Panel panel, DateOnly date, double fraction, string valueText)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        var label = new TextBlock { Text = date.ToString("dd.MM", CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center };
        // Доля задаётся звёздными колонками: полоса тянется вместе с окном
        var fill = new Border { Background = (Brush)FindResource("PrimaryBrush"), CornerRadius = new CornerRadius(5), MinWidth = 4 };
        var bar = new Grid();
        var share = Math.Clamp(fraction, 0.02, 1);
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share + 0.0001, GridUnitType.Star) });
        bar.Children.Add(fill);
        var track = new Border
        {
            Background = (Brush)FindResource("CardAltBrush"),
            CornerRadius = new CornerRadius(5),
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Child = bar
        };
        var value = new TextBlock
        {
            Text = valueText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = (Brush)FindResource("MutedTextBrush")
        };
        Grid.SetColumn(track, 1);
        Grid.SetColumn(value, 2);
        row.Children.Add(label);
        row.Children.Add(track);
        row.Children.Add(value);
        panel.Children.Add(row);
    }

    private void DailyGoalCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
            RefreshProgress();
        }
    }

    private void ClearHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Texts.T("Удалить все записи о тренировках на этом компьютере?"), Texts.T("Очистить историю"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _historyStore.Clear();
        _history = Array.Empty<TrainingRecord>();
        RefreshProgress();
    }

    private void AlphabetCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateKochSummary();
        UpdateModeButtons();
        if (_windowLoaded)
        {
            RefreshCourse();
        }
    }

    private void KochLevelSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateKochSummary();
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    private void UpdateKochSummary()
    {
        if (KochSummaryText is null || KochLevelSlider is null || AlphabetCombo is null)
        {
            return;
        }

        var alphabet = (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2);
        KochLevelSlider.Maximum = KochMethod.MaxLevel(alphabet);
        KochSummaryText.Text = KochMethod.Describe(alphabet, (int)KochLevelSlider.Value);
    }

    private void FarnsworthButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();
        TrainingPresets.ApplyFarnsworth(settings);
        SpeedSlider.Value = settings.CharactersPerMinute;
        CharacterGapSlider.Value = settings.CharacterGapUnits;
        GroupGapSlider.Value = settings.GroupGapUnits;
        UpdateSettingLabels();
        ResultDetailsText.Text = TrainingPresets.FarnsworthDescription;
        ResultDetailsText.Foreground = (Brush)FindResource("MutedTextBrush");
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    // ---------- Передача ключом ----------

    private void EnsureKeyer()
    {
        var speed = (int)SpeedSlider.Value;
        // Позывные и Q-код всегда декодируются латиницей
        var alphabet = (int)ContentModes.DecodingAlphabet(
            ContentModes.Clamp(ContentModeCombo.SelectedIndex), (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2));
        if (_keyer is null || _keyerSpeed != speed || _keyerAlphabet != alphabet)
        {
            var text = _keyer?.Text ?? string.Empty;
            _keyer = new KeyerDecoder((AlphabetMode)alphabet, speed);
            _keyerSpeed = speed;
            _keyerAlphabet = alphabet;
            KeyerTimingText.Text = Texts.F("Точка: {0:0} мс · тире от {1:0} мс", _keyer.UnitMilliseconds, _keyer.UnitMilliseconds * KeyerDecoder.DashThresholdUnits);
            if (text.Length > 0)
            {
                KeyerOutputText.Text = text;
            }
        }

        if (_keyerTimer is null)
        {
            _keyerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _keyerTimer.Tick += KeyerTimer_OnTick;
            _keyerTimer.Start();
        }
    }

    private void StartKeyPress()
    {
        if (_keyDown)
        {
            return;
        }

        EnsureKeyer();
        _keyDown = true;
        var now = _keyerClock.Elapsed.TotalMilliseconds;
        if (_keyerLastRelease >= 0)
        {
            _keyer!.Idle(now - _keyerLastRelease);
        }

        _keyerPressStart = now;
        StartKeyerTone();
        UpdateKeyerDisplay();
    }

    private void EndKeyPress()
    {
        if (!_keyDown)
        {
            return;
        }

        _keyDown = false;
        _tonePlayer?.Stop();
        var now = _keyerClock.Elapsed.TotalMilliseconds;
        _keyer?.Press(now - _keyerPressStart);
        _keyerLastRelease = now;
        UpdateKeyerDisplay();
    }

    private void KeyerTimer_OnTick(object? sender, EventArgs e)
    {
        if (_keyDown || _keyer is null || _keyerLastRelease < 0)
        {
            return;
        }

        var before = _keyer.Text.Length + _keyer.PendingCode.Length;
        _keyer.Idle(_keyerClock.Elapsed.TotalMilliseconds - _keyerLastRelease);
        if (_keyer.Text.Length + _keyer.PendingCode.Length != before || _keyer.PendingCode.Length == 0)
        {
            UpdateKeyerDisplay();
        }
    }

    private void StartKeyerTone()
    {
        var frequency = (int)FrequencySlider.Value;
        var volume = (int)VolumeSlider.Value;
        if (_tonePlayer is null || _toneFrequency != frequency || _toneVolume != volume)
        {
            StopKeyerTone();
            _toneStream = new MemoryStream(MorseAudioService.RenderTone(1, frequency, volume).WavBytes);
            _tonePlayer = new SoundPlayer(_toneStream);
            _tonePlayer.Load();
            _toneFrequency = frequency;
            _toneVolume = volume;
        }

        _tonePlayer.PlayLooping();
    }

    private void StopKeyerTone()
    {
        _tonePlayer?.Stop();
        _tonePlayer?.Dispose();
        _tonePlayer = null;
        _toneStream?.Dispose();
        _toneStream = null;
    }

    private void UpdateKeyerDisplay()
    {
        if (_keyer is null)
        {
            return;
        }

        var pending = _keyer.PendingCode.Replace('.', '•').Replace('-', '—');
        KeyerCodeText.Text = pending.Length == 0 ? " " : pending;
        KeyerOutputText.Text = _keyer.Text;
        KeyerOutputText.CaretIndex = KeyerOutputText.Text.Length;
        UpdateKeyerTarget();
    }

    /// <summary>Задание в «Передаче»: уже переданное верно подряд с начала подсвечено, остальное приглушено.</summary>
    private void UpdateKeyerTarget()
    {
        KeyerTargetText.Inlines.Clear();
        if (_keyerTarget.Length == 0)
        {
            return;
        }

        var matched = KeyerProgress.MatchedLength(_keyerTarget, _keyer?.Text);
        if (matched > 0)
        {
            var done = new Run(_keyerTarget[..matched]) { FontWeight = FontWeights.Bold };
            done.SetResourceReference(TextElement.ForegroundProperty, "PrimaryBrush");
            KeyerTargetText.Inlines.Add(done);
        }

        if (matched < _keyerTarget.Length)
        {
            KeyerTargetText.Inlines.Add(new Run(_keyerTarget[matched..]));
        }
    }

    private void KeyPad_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        KeyPad.CaptureMouse();
        StartKeyPress();
        e.Handled = true;
    }

    private void KeyPad_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        KeyPad.ReleaseMouseCapture();
        EndKeyPress();
        e.Handled = true;
    }

    private void KeyPad_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_keyDown && !KeyPad.IsMouseCaptured)
        {
            EndKeyPress();
        }
    }

    private void KeyPad_OnLostMouseCapture(object sender, MouseEventArgs e) => EndKeyPress();

    private void KeyerModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyerNewTaskButton is null || KeyerCheckButton is null || KeyerTargetText is null)
        {
            return;
        }

        var taskMode = KeyerModeCombo.SelectedIndex == 1;
        KeyerNewTaskButton.Visibility = taskMode ? Visibility.Visible : Visibility.Collapsed;
        KeyerCheckButton.Visibility = taskMode ? Visibility.Visible : Visibility.Collapsed;
        if (!taskMode)
        {
            _keyerTarget = string.Empty;
            KeyerTargetText.Text = string.Empty;
            KeyerResultText.Text = string.Empty;
        }
        else if (_keyerTarget.Length == 0)
        {
            KeyerNewTaskButton_OnClick(sender, e);
        }
    }

    private void KeyerNewTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();
        var alphabet = (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2);
        var content = ContentModes.Clamp(settings.ContentModeIndex);
        var pool = MorseAlphabet.BuildPool(alphabet, content, _selectedSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            KeyerResultText.Text = Texts.T("В выбранном наборе нет символов: измените состав задания на вкладке «Тренировка».");
            return;
        }

        _keyerTarget = TrainingGenerator.GenerateTask(content, alphabet, pool, Math.Clamp(Math.Min(settings.GroupCount, 4), 1, 4));
        UpdateKeyerTarget();
        KeyerResultText.Text = Texts.T("Передайте текст выше, затем нажмите «Проверить передачу».");
        KeyerResultText.Foreground = (Brush)FindResource("MutedTextBrush");
        KeyerClearButton_OnClick(sender, e);
    }

    private void KeyerCheckButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_keyerTarget.Length == 0 || _keyer is null)
        {
            return;
        }

        _keyer.CommitSymbol();
        UpdateKeyerDisplay();
        var result = TrainingEvaluator.Evaluate(_keyerTarget, _keyer.Text);
        if (result.IsPerfect)
        {
            KeyerResultText.Text = Texts.T("Отлично: передано без ошибок.");
            KeyerResultText.Foreground = (Brush)FindResource("PrimaryBrush");
        }
        else
        {
            var details = result.Mistakes.Take(8).Select(mistake => $"{mistake.Position}: {mistake.Expected}→{mistake.Actual?.ToString() ?? "∅"}");
            KeyerResultText.Text = Texts.F("Точность {0:0.#}%. Ошибки: {1}{2}", result.AccuracyPercent, string.Join(", ", details), result.Mistakes.Count > 8 ? " …" : string.Empty);
            KeyerResultText.Foreground = (Brush)FindResource("DangerBrush");
        }

        ShowKeyerAnalysis();
    }

    private void KeyerAnalyzeButton_OnClick(object sender, RoutedEventArgs e) => ShowKeyerAnalysis();

    private void ShowKeyerAnalysis()
    {
        if (_keyer is null || KeyerAnalysisText is null)
        {
            return;
        }

        var analysis = _keyer.Analyze();
        KeyerAnalysisText.Text = analysis.Describe();
        KeyerAnalysisText.Foreground = (Brush)FindResource(analysis.HasEnoughData ? "TextBrush" : "MutedTextBrush");
    }

    private void KeyerBackspaceButton_OnClick(object sender, RoutedEventArgs e)
    {
        _keyer?.Backspace();
        UpdateKeyerDisplay();
    }

    private void KeyerClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        _keyer?.Clear();
        _keyerLastRelease = -1;
        UpdateKeyerDisplay();
        if (KeyerCodeText is not null)
        {
            KeyerCodeText.Text = " ";
        }
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

    private async void GenerateButton_OnClick(object sender, RoutedEventArgs e)
    {
        await GenerateTaskAsync();
    }

    private void TogglePanelButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetTrainingPanelCollapsed(TrainingPanel.Visibility == Visibility.Visible);
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    /// <summary>Свёрнутая панель параметров отдаёт ширину заданию; «Новое задание» переезжает в шапку.</summary>
    private void SetTrainingPanelCollapsed(bool collapsed)
    {
        TrainingPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        TrainingPanelColumn.Width = new GridLength(collapsed ? 0 : 380);
        TrainingPanelSpacer.Width = new GridLength(collapsed ? 0 : 18);
        QuickGenerateButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        TogglePanelButton.Content = collapsed ? Texts.T("Параметры ▶") : Texts.T("◀ Параметры");
    }

    // Колонки истории делят ширину таблицы по долям — без горизонтальной прокрутки на маленьком окне
    private static readonly double[] HistoryColumnShares = { 0.21, 0.22, 0.13, 0.09, 0.14, 0.21 };

    private void HistoryListView_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || HistoryListView.View is not GridView view)
        {
            return;
        }

        var available = Math.Max(280, HistoryListView.ActualWidth - SystemParameters.VerticalScrollBarWidth - 14);
        for (var index = 0; index < view.Columns.Count && index < HistoryColumnShares.Length; index++)
        {
            view.Columns[index].Width = Math.Floor(available * HistoryColumnShares[index]);
        }
    }

    /// <summary>Новое задание по параметрам; drill — упражнение «Повторить сложные символы» вместо обычного состава.</summary>
    private async Task GenerateTaskAsync(DrillPlan? drill = null)
    {
        if (!TryReadGroupCount(out var groupCount))
        {
            return;
        }

        var settings = ReadSettings();
        settings.GroupCount = groupCount;
        var alphabet = (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2);
        var content = ContentModes.Clamp(ContentModeCombo.SelectedIndex);
        var pool = drill?.Pool ?? MorseAlphabet.BuildPool(alphabet, content, _selectedSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            MessageBox.Show(
                Texts.T("В выбранном наборе нет символов. Откройте выбор и отметьте хотя бы одну букву или цифру."),
                Texts.T("Не удалось создать задание"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        StopPlayback();
        EndExam();
        SetGenerationState(isGenerating: true);

        try
        {
            // Чаще звучат новый символ метода Коха и символы с ошибками из истории
            var emphasized = new HashSet<char>();
            if (drill is not null)
            {
                // Упражнение на ошибки: сами сложные символы звучат втрое чаще похожих на них
                emphasized.UnionWith(drill.Problems);
            }
            else if (content == ContentMode.Koch)
            {
                emphasized.Add(pool[^1]);
            }

            if (drill is null && settings.EmphasizeProblemSymbols)
            {
                foreach (var problem in TrainingStatistics.ProblemSymbols(_history, 6))
                {
                    if (pool.Contains(problem.Symbol))
                    {
                        emphasized.Add(problem.Symbol);
                    }
                }
            }

            var generatedTask = drill is null
                ? TrainingGenerator.GenerateTask(content, alphabet, pool, settings.GroupCount, emphasized)
                : TrainingGenerator.Generate(pool, settings.GroupCount, emphasized);
            var audio = await Task.Run(() => MorseAudioService.Render(
                generatedTask,
                settings.CharactersPerMinute,
                settings.FrequencyHz,
                settings.VolumePercent,
                settings.CharacterGapUnits,
                settings.GroupGapUnits,
                settings.PlayStartSignal,
                settings.StartPauseUnits,
                new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz)));

            _currentTask = generatedTask;
            _currentTaskRecorded = false;
            _currentTaskStartedAt = DateTime.Now;
            _currentClip = audio;
            _currentSettings = settings;
            _currentDrill = drill;
            _answerVisible = false;
            UserAnswerText.Clear();
            ResultDetailsText.Text = drill?.Describe() ?? Texts.T("Пробелы между группами при проверке не учитываются");
            ResultDetailsText.Foreground = (Brush)FindResource("MutedTextBrush");
            AccuracyText.Text = "—";
            PlaybackProgress.Value = 0;
            PlaybackStatusText.Text = Texts.F("Готово к воспроизведению · {0}", FormatDuration(audio.Duration));
            var startSignal = settings.PlayStartSignal ? Texts.T(" · старт Ж Ж Ж") : string.Empty;
            if (!new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz).IsClean)
            {
                startSignal += Texts.T(" · помехи");
            }

            var metaTemplate = drill is null && ContentModes.IsWordMode(content)
                ? Texts.T("{0} слов · {1} знаков/мин · паузы {2}/{3}{4}")
                : Texts.T("{0} групп × 5 · {1} знаков/мин · паузы {2}/{3}{4}");
            TaskMetaText.Text = (drill is null ? string.Empty : Texts.T("Повтор сложных · ")) +
                                string.Format(CultureInfo.CurrentCulture, metaTemplate, settings.GroupCount, settings.CharactersPerMinute, settings.CharacterGapUnits, settings.GroupGapUnits, startSignal);
            UpdateAnswerDisplay();
            SetStatus(Texts.T("ГОТОВО"), isActive: true);
            SetTaskControlsEnabled(true);
            _settingsService.Save(settings);
        }
        catch (Exception exception)
        {
            _currentTask = string.Empty;
            _currentClip = null;
            _currentSettings = null;
            SetTaskControlsEnabled(false);
            SetStatus(Texts.T("ОШИБКА"), isActive: false);
            PlaybackStatusText.Text = Texts.T("Не удалось подготовить звук");
            MessageBox.Show(Texts.F("Не удалось создать задание.\n\n{0}", exception.Message), "Morse Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetGenerationState(isGenerating: false);
        }
    }

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        await PlayCurrentAsync();
    }

    private bool CanPlayNow => _currentClip is not null && (_exam is null || _exam.CanPlay);

    private async Task PlayCurrentAsync()
    {
        if (_currentClip is null || (_exam is not null && !_exam.CanPlay))
        {
            return;
        }

        _exam?.RegisterPlayback();
        UpdateExamStatus();
        StopLearningPlayback();
        StopPlayback();
        var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        _audioStream = new MemoryStream(_currentClip.WavBytes, writable: false);
        _player = new SoundPlayer(_audioStream);

        try
        {
            _player.Load();
            _player.Play();
            PlayButton.IsEnabled = false;
            RepeatButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            PlaybackProgress.Value = 0;
            PlaybackStatusText.Text = Texts.T("Идёт воспроизведение…");
            SetStatus(Texts.T("СЛУШАЕМ"), isActive: true);

            var startedAt = DateTime.UtcNow;
            while (DateTime.UtcNow - startedAt < _currentClip.Duration)
            {
                await Task.Delay(50, cancellation.Token);
                var elapsed = DateTime.UtcNow - startedAt;
                PlaybackProgress.Value = Math.Min(100, elapsed.TotalMilliseconds / _currentClip.Duration.TotalMilliseconds * 100);
            }

            if (!cancellation.IsCancellationRequested)
            {
                PlaybackProgress.Value = 100;
                MarkPracticed();
                // Фокус в поле ответа — только при вводе; при записи на бумаге остаётся сверка с текстом задания
                if (AnswerInputPanel.Visibility == Visibility.Visible)
                {
                    PlaybackStatusText.Text = Texts.T("Прослушивание завершено — введите ответ");
                    SetStatus(Texts.T("ВАШ ОТВЕТ"), isActive: true);
                    UserAnswerText.Focus();
                }
                else
                {
                    PlaybackStatusText.Text = Texts.T("Готово — сверьте запись с текстом задания");
                    SetStatus(Texts.T("СВЕРЬТЕ"), isActive: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MessageBox.Show(Texts.F("Не удалось воспроизвести звук.\n\n{0}", exception.Message), "Morse Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                PlayButton.IsEnabled = CanPlayNow;
                RepeatButton.IsEnabled = CanPlayNow;
                StopButton.IsEnabled = false;
                _playbackCancellation = null;
            }
        }
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e) => StopPlayback();

    /// <summary>Занятие без записи в истории (задание дослушано, ответ «На слух»): плашка и напоминание его учитывают.</summary>
    private void MarkPracticed()
    {
        _lastPracticeAt = DateTime.Now;
        NudgeBanner.Visibility = Visibility.Collapsed;
    }

    private void AnswerInputButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetAnswerInput(AnswerInputPanel.Visibility != Visibility.Visible, remember: true);
        SaveSettingsIfLoaded();
        if (AnswerInputPanel.Visibility == Visibility.Visible && UserAnswerText.IsEnabled)
        {
            UserAnswerText.Focus();
        }
    }

    /// <summary>
    /// Ввод ответа — по желанию: обычно группы пишут на бумаге и сверяют с текстом задания («Показать»).
    /// С вводом видны точность и попытки и появляется вкладка «Прогресс»; экзамен и шаги курса засчитываются
    /// по введённому ответу — там ввод раскрывается сам, без запоминания.
    /// </summary>
    private void SetAnswerInput(bool visible, bool remember)
    {
        if (remember)
        {
            _answerInputPreferred = visible;
        }

        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        AnswerInputPanel.Visibility = visibility;
        AnswerStatsGrid.Visibility = visibility;
        ProgressTab.Visibility = visibility;
        PaperHintText.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        AnswerInputButton.Content = visible ? Texts.T("Проверить вводом ▴") : Texts.T("Проверить вводом ▾");
    }

    private void StopPlayback(bool resetProgress = true)
    {
        _playbackCancellation?.Cancel();
        _playbackCancellation = null;
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _audioStream?.Dispose();
        _audioStream = null;

        if (resetProgress && PlaybackProgress is not null)
        {
            PlaybackProgress.Value = 0;
            if (_currentClip is not null)
            {
                PlaybackStatusText.Text = Texts.F("Готово к воспроизведению · {0}", FormatDuration(_currentClip.Duration));
                SetStatus(Texts.T("ГОТОВО"), isActive: true);
            }
        }

        if (PlayButton is not null)
        {
            PlayButton.IsEnabled = CanPlayNow;
            RepeatButton.IsEnabled = CanPlayNow;
            StopButton.IsEnabled = false;
        }
    }

    private void ToggleAnswerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            return;
        }

        _answerVisible = !_answerVisible;
        UpdateAnswerDisplay();
    }

    private void UpdateAnswerDisplay()
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            AnswerDisplayText.Text = "••••• ••••• •••••";
            ToggleAnswerButton.Content = Texts.T("Показать");
            return;
        }

        AnswerDisplayText.Text = _answerVisible
            ? _currentTask
            : new string(_currentTask.Select(symbol => char.IsWhiteSpace(symbol) ? ' ' : '\u2022').ToArray());
        ToggleAnswerButton.Content = _answerVisible ? Texts.T("Скрыть") : Texts.T("Показать");
    }

    private void CheckAnswerButton_OnClick(object sender, RoutedEventArgs e) => CheckAnswer();

    private void CheckAnswer()
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            return;
        }

        var result = TrainingEvaluator.Evaluate(_currentTask, UserAnswerText.Text);
        _attempts++;
        AttemptsText.Text = _attempts.ToString(CultureInfo.InvariantCulture);
        // Экзамен завершается первой проверкой: время останавливается, протокол готов
        ExamResult? examResult = null;
        if (_exam is not null && !_exam.IsFinished)
        {
            examResult = _exam.Finish(UserAnswerText.Text, DateTime.Now);
            _examTimer?.Stop();
            _examReport = ExamReport.Format(examResult);
            SaveTaskButton.Content = Texts.T("Сохранить протокол");
            TaskMetaText.Text = Texts.F("Экзамен завершён · {0}", ExamReport.FormatDuration(examResult.Duration));
            ToggleAnswerButton.IsEnabled = true;
        }

        // В историю попадает только первая проверка каждого задания
        if (!_currentTaskRecorded)
        {
            _currentTaskRecorded = true;
            var taskSettings = _currentSettings ?? ReadSettings();
            // Задание по настройкам текущего шага курса помечается его номером — так считается зачёт шага
            var courseStep = _currentDrill is null ? Course.StepForRecord(taskSettings, examResult is not null) : 0;
            var record = TrainingStatistics.CreateRecord(DateTime.Now, taskSettings.ActiveProfileName,
                taskSettings.CharactersPerMinute, _currentTask.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, result, examResult is not null,
                DateTime.Now - _currentTaskStartedAt, courseStep);
            _history = _historyStore.Add(record);
            RefreshProgress();
            RefreshCourse();
            // Лестница скорости: по истории этого профиля
            if (taskSettings.AutoSpeed)
            {
                var nextSpeed = SpeedLadder.Next(_history, taskSettings.CharactersPerMinute, taskSettings.ActiveProfileName);
                if (nextSpeed != taskSettings.CharactersPerMinute)
                {
                    SpeedSlider.Value = nextSpeed;
                    UpdateSettingLabels();
                    _autoSpeedNote = SpeedLadder.Describe(taskSettings.CharactersPerMinute, nextSpeed);
                    _settingsService.Save(ReadSettings());
                }
            }
        }
        AccuracyText.Text = $"{result.AccuracyPercent:0.#}%";

        foreach (var mistake in result.Mistakes.Where(item => item.Expected != '\u2205'))
        {
            _problemSymbols[mistake.Expected] = _problemSymbols.GetValueOrDefault(mistake.Expected) + 1;
        }

        ProblemSymbolsText.Text = _problemSymbols.Count == 0
            ? "—"
            : string.Join("  ", _problemSymbols.OrderByDescending(item => item.Value).ThenBy(item => item.Key).Take(6)
                .Select(item => $"{item.Key} ×{item.Value}"));

        if (result.IsPerfect)
        {
            ResultDetailsText.Text = Texts.T("Отлично: все символы распознаны правильно");
            ResultDetailsText.Foreground = (Brush)FindResource("PrimaryBrush");
            SetStatus(Texts.T("БЕЗ ОШИБОК"), isActive: true);
        }
        else
        {
            var details = result.Mistakes.Take(8)
                .Select(mistake => $"{mistake.Position}: {mistake.Expected}→{mistake.Actual?.ToString() ?? "∅"}");
            var suffix = result.Mistakes.Count > 8 ? " …" : string.Empty;
            ResultDetailsText.Text = Texts.F("Ошибки: {0}{1}", string.Join(", ", details), suffix);
            ResultDetailsText.Foreground = (Brush)FindResource("DangerBrush");
            SetStatus(Texts.T("ЕСТЬ ОШИБКИ"), isActive: false);
        }

        if (examResult is not null)
        {
            ResultDetailsText.Text = ExamReport.Summary(examResult) + "\n" +
                                     TrainingStatistics.Exams(TrainingStatistics.ForProfile(_history, examResult.ProfileName)).Describe();
            SetStatus(result.IsPerfect ? Texts.T("ЭКЗАМЕН СДАН") : Texts.T("ЕСТЬ ОШИБКИ"), result.IsPerfect);
        }
        else if (_currentDrill is null && _currentSettings?.ContentModeIndex == (int)ContentMode.Koch)
        {
            var kochAlphabet = (AlphabetMode)Math.Clamp(_currentSettings.AlphabetIndex, 0, 2);
            ResultDetailsText.Text += "\n" + KochMethod.Advice(kochAlphabet, _currentSettings.KochLevel, result.AccuracyPercent);
        }

        if (_autoSpeedNote.Length > 0)
        {
            ResultDetailsText.Text += "\n" + _autoSpeedNote;
            _autoSpeedNote = string.Empty;
        }

        _answerVisible = true;
        UpdateAnswerDisplay();
    }

    // ---------- Повтор сложных символов ----------

    private async void DrillButton_OnClick(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 0;
        var plan = ProblemDrill.Build(_history);
        if (plan.IsEmpty)
        {
            // Подсказка вместо окна: ничего не блокирует, как у пресета Фарнсворта
            ResultDetailsText.Text = Texts.T("Ошибок в истории пока нет. Пройдите несколько заданий: символы, в которых вы ошибётесь, и похожие на них по коду попадут в это упражнение.");
            ResultDetailsText.Foreground = (Brush)FindResource("MutedTextBrush");
            return;
        }

        await GenerateTaskAsync(plan);
    }

    // ---------- Экзамен ----------

    private async void ExamButton_OnClick(object sender, RoutedEventArgs e) => await StartExamAsync();

    private async Task StartExamAsync()
    {
        MainTabs.SelectedIndex = 0;
        SetAnswerInput(true, remember: false);
        await GenerateTaskAsync();
        if (string.IsNullOrEmpty(_currentTask) || _currentSettings is null || _currentClip is null)
        {
            return;
        }

        var groupCount = _currentTask.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        _exam = new ExamSession(_currentTask, _currentSettings.CharactersPerMinute, groupCount, _currentSettings.ActiveProfileName, DateTime.Now,
            _currentSettings.ExamPlaybacks, _currentSettings.ExamTimeLimitMinutes);
        _examReport = null;
        _answerVisible = false;
        UpdateAnswerDisplay();
        // Ответ скрыт до проверки; повтор доступен, пока не кончились прослушивания
        ToggleAnswerButton.IsEnabled = false;
        RepeatButton.IsEnabled = false;
        if (_examTimer is null)
        {
            _examTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _examTimer.Tick += ExamTimer_OnTick;
        }

        _examTimer.Start();
        UpdateExamStatus();
        ResultDetailsText.Text = Texts.F("Правила экзамена: {0}", ExamReport.Rules(_exam.MaxPlaybacks, _exam.TimeLimit));
        ResultDetailsText.Foreground = (Brush)FindResource("MutedTextBrush");
        SetStatus(Texts.T("ЭКЗАМЕН"), isActive: true);
        await PlayCurrentAsync();
    }

    private void ExamTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateExamStatus();
        // Лимит времени вышел — ответ проверяется сам, как по кнопке
        if (_exam is { IsFinished: false } && _exam.IsTimeUp(DateTime.Now))
        {
            StopPlayback();
            CheckAnswer();
        }
    }

    private void UpdateExamStatus()
    {
        if (_exam is null || _exam.IsFinished)
        {
            return;
        }

        TaskMetaText.Text = _exam.Status(DateTime.Now);
    }

    /// <summary>Новое задание отменяет экзамен: таймер останавливается, кнопки возвращаются в обычный режим.</summary>
    private void EndExam()
    {
        _examTimer?.Stop();
        _exam = null;
        _examReport = null;
        if (SaveTaskButton is not null)
        {
            SaveTaskButton.Content = Texts.T("Сохранить TXT");
        }
    }

    private void SelectSymbolsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SymbolSelectionWindow(_selectedSymbols) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _selectedSymbols = dialog.SelectedSymbols;
            UpdateSelectedSymbolsSummary();
            _settingsService.Save(ReadSettings());
        }
    }

    private void UpdateSelectedSymbolsSummary()
    {
        UpdateModeButtons();
        SelectedSymbolsSummaryText.Text = string.IsNullOrEmpty(_selectedSymbols)
            ? Texts.T("Ничего не выбрано")
            : Texts.F("{0} символов: {1}", _selectedSymbols.Length, Truncate(_selectedSymbols, 45));
    }

    private void SaveTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            return;
        }

        if (_examReport is not null)
        {
            var reportDialog = new SaveFileDialog
            {
                Title = Texts.T("Сохранить протокол"),
                Filter = Texts.T("Протокол экзамена (*.txt)|*.txt"),
                FileName = $"morse-exam-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
                AddExtension = true
            };
            if (reportDialog.ShowDialog(this) == true)
            {
                File.WriteAllText(reportDialog.FileName, _examReport, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }

            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Сохранить задание"),
            Filter = Texts.T("Текстовый файл (*.txt)|*.txt"),
            FileName = $"morse-task-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var settings = _currentSettings ?? ReadSettings();
        var content = new StringBuilder()
            .AppendLine("Morse Trainer")
            .AppendLine(Texts.F("Создано: {0:dd.MM.yyyy HH:mm}", DateTime.Now))
            .AppendLine(_currentDrill is null && ContentModes.IsWordMode(ContentModes.Clamp(settings.ContentModeIndex))
                ? Texts.F("Слов: {0}", settings.GroupCount)
                : Texts.F("Групп: {0} × 5", settings.GroupCount))
            .AppendLine(Texts.F("Скорость: {0} знаков/мин", settings.CharactersPerMinute))
            .AppendLine(Texts.F("Тональность: {0} Гц", settings.FrequencyHz))
            .AppendLine(Texts.F("Паузы: символы {0}, группы {1}", settings.CharacterGapUnits, settings.GroupGapUnits))
            .AppendLine(settings.PlayStartSignal
                ? Texts.F("Старт: Ж Ж Ж, затем пауза {0} точек", settings.StartPauseUnits)
                : Texts.T("Стартовый сигнал: выключен"))
            .AppendLine().AppendLine(Texts.T("Задание:")).AppendLine(_currentTask).ToString();
        File.WriteAllText(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private void ExportWavButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentClip is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Экспортировать звук"),
            Filter = Texts.T("Звуковой файл WAV (*.wav)|*.wav"),
            FileName = $"morse-task-{DateTime.Now:yyyy-MM-dd-HHmm}.wav",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllBytes(dialog.FileName, _currentClip.WavBytes);
        }
    }

    // ---------- Кнопки выбора вместо выпадающих списков (как на телефоне) ----------

    private Button[] LearningAlphabetButtons => new[] { LearningRussianButton, LearningLatinButton, LearningBothButton, LearningDigitsButton };

    private Button[] QuizAlphabetButtons => new[] { QuizRussianButton, QuizLatinButton, QuizBothButton, QuizDigitsButton };

    private Button[] QuizAnswerButtons => new[] { QuizAnswer0Button, QuizAnswer1Button, QuizAnswer2Button, QuizAnswer3Button };

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

    private void LearningAlphabetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var index = ChoiceIndex(sender, 3);
        if (index < 0)
        {
            return;
        }

        _learningAlphabet = index;
        HighlightChoice(LearningAlphabetButtons, _learningAlphabet);
        RefreshLearningItems();
        SaveSettingsIfLoaded();
    }

    private void LearningAudioModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var mode = ChoiceIndex(sender, 1);
        if (mode < 0)
        {
            return;
        }

        _learningAudioMode = mode;
        UpdateLearningAudioModeButtons();
        SaveSettingsIfLoaded();
    }

    // Кнопки в порядке «Голос + сигнал» (режим 1), «Напев на экране + сигнал» (режим 0)
    private void UpdateLearningAudioModeButtons() =>
        HighlightChoice(new[] { LearningVoiceModeButton, LearningChantModeButton }, _learningAudioMode == 1 ? 0 : 1);

    private void LearningFilter_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshLearningItemsIfReady();
    }

    private void RefreshLearningItemsIfReady()
    {
        if (LearningItemsControl is not null)
        {
            RefreshLearningItems();
        }
    }

    private void RefreshLearningItems()
    {
        var search = LearningSearchText?.Text?.Trim() ?? string.Empty;
        var items = LearningCatalog.GetItems(_learningAlphabet);
        _visibleLearningItems = string.IsNullOrEmpty(search)
            ? items
            : items.Where(item => item.Symbol.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || item.Chant.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        LearningItemsControl.ItemsSource = _visibleLearningItems;
    }

    // ---------- Свои напевы и голос ----------

    private void LearningCardEdit_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: char symbol } || LearningCatalog.Find(symbol) is not { } item)
        {
            return;
        }

        var dialog = new ChantEditorWindow(item) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            LearningCatalog.SetCustomChants(_chantStore.Set(symbol, dialog.Result));
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Изменить напев"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshLearningItems();
        UpdateCustomVoiceText();
        if (LearningNowSymbolText.Text == symbol.ToString() && LearningCatalog.Find(symbol) is { } updated)
        {
            ShowHighlightedChant(updated.Chant, -1);
        }
    }

    private void OpenVoiceFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.VoiceDirectory);
            Process.Start(new ProcessStartInfo(AppPaths.VoiceDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Папка своего голоса"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateCustomVoiceText();
    }

    private void ResetChantsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var count = LearningCatalog.CustomChants.Count;
        if (count == 0)
        {
            LearningPlaybackStatusText.Text = Texts.T("Своих напевов нет — звучат встроенные.");
            return;
        }

        var answer = MessageBox.Show(this, Texts.F("Удалить свои напевы ({0}) и вернуть встроенные?", count), Texts.T("Вернуть встроенные напевы"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var empty = new Dictionary<char, string>();
        _chantStore.Save(empty);
        LearningCatalog.SetCustomChants(empty);
        RefreshLearningItems();
        UpdateCustomVoiceText();
    }

    private void UpdateCustomVoiceText()
    {
        var chants = LearningCatalog.CustomChants.Count;
        CustomVoiceText.Text = CustomVoice.Describe(CustomVoice.Count(AppPaths.VoiceDirectory)) +
                               (chants > 0 ? " " + Texts.F("Своих напевов: {0}.", chants) : string.Empty);
    }

    private async void LearningCardPlay_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: char symbol })
        {
            var item = LearningCatalog.Find(symbol);
            if (item is not null)
            {
                await PlayLearningItemAsync(item);
            }
        }
    }

    private async Task PlayLearningItemAsync(LearningSymbolItem item)
    {
        StopPlayback();
        StopLearningPlayback();
        var cancellation = new CancellationTokenSource();
        _learningCancellation = cancellation;
        LearningNowSymbolText.Text = item.Symbol.ToString();
        LearningNowCodeText.Text = item.Code;
        ShowHighlightedChant(item.Chant, -1);

        try
        {
            if (_learningAudioMode == 1)
            {
                LearningPlaybackStatusText.Text = Texts.T("Произносим напев…");
                _learningAudioStream = VoicePackService.Open(item.Symbol);
                if (_learningAudioStream is null)
                {
                    LearningPlaybackStatusText.Text = Texts.T("Офлайн-напев недоступен — воспроизводим сигнал");
                }
                else
                {
                    var voiceStream = _learningAudioStream;
                    var voicePlayer = new SoundPlayer(voiceStream);
                    _learningPlayer = voicePlayer;
                    voicePlayer.Load();
                    await Task.Run(voicePlayer.PlaySync, cancellation.Token);
                    voicePlayer.Dispose();
                    voiceStream.Dispose();
                    if (ReferenceEquals(_learningPlayer, voicePlayer))
                    {
                        _learningPlayer = null;
                    }
                    if (ReferenceEquals(_learningAudioStream, voiceStream))
                    {
                        _learningAudioStream = null;
                    }
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
            const int learningSpeed = 45;
            var clip = MorseAudioService.Render(item.Symbol.ToString(), learningSpeed, (int)FrequencySlider.Value,
                (int)VolumeSlider.Value, 3, 7);
            _learningAudioStream = new MemoryStream(clip.WavBytes, writable: false);
            _learningPlayer = new SoundPlayer(_learningAudioStream);
            _learningPlayer.Load();
            _learningPlayer.Play();
            LearningPlaybackStatusText.Text = Texts.T("Слушаем ритм символа…");

            var dotMilliseconds = 6_000d / learningSpeed;
            for (var index = 0; index < item.Code.Length; index++)
            {
                ShowHighlightedChant(item.Chant, index);
                var units = item.Code[index] == '.' ? 1 : 3;
                await Task.Delay(TimeSpan.FromMilliseconds(dotMilliseconds * units), cancellation.Token);
                if (index < item.Code.Length - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(dotMilliseconds), cancellation.Token);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(dotMilliseconds * 2), cancellation.Token);
            ShowHighlightedChant(item.Chant, -1);
            LearningPlaybackStatusText.Text = Texts.T("Готово — можно прослушать ещё раз");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_learningCancellation, cancellation))
            {
                _learningCancellation = null;
            }
        }
    }

    private void ShowHighlightedChant(string chant, int activeIndex)
    {
        LearningHighlightedChantText.Inlines.Clear();
        var syllables = chant.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < syllables.Length; index++)
        {
            if (index > 0)
            {
                LearningHighlightedChantText.Inlines.Add(new Run("-"));
            }

            LearningHighlightedChantText.Inlines.Add(new Run(syllables[index])
            {
                Foreground = (Brush)FindResource(index == activeIndex ? "PrimaryBrush" : "TextBrush"),
                FontWeight = index == activeIndex ? FontWeights.Bold : FontWeights.SemiBold
            });
        }
    }

    // ---------- На слух ----------

    private void QuizAlphabetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var index = ChoiceIndex(sender, 3);
        if (index < 0)
        {
            return;
        }

        CancelQuizNext();
        StopLearningPlayback();
        _quizAlphabet = index;
        HighlightChoice(QuizAlphabetButtons, _quizAlphabet);
        // Вопрос из прежнего набора больше не подходит
        _quizTarget = null;
        _quizAnswered = true;
        foreach (var button in QuizAnswerButtons)
        {
            button.Content = "?";
            button.Tag = null;
            button.IsEnabled = false;
            ClearQuizMark(button);
        }

        QuizRepeatButton.IsEnabled = false;
        QuizStatusText.Text = Texts.T("Нажмите «Новый символ» и слушайте");
        QuizStatusText.ClearValue(TextBlock.ForegroundProperty);
        SaveSettingsIfLoaded();
    }

    private async void QuizNewButton_OnClick(object sender, RoutedEventArgs e) => await AskQuizAsync();

    private async void QuizRepeatButton_OnClick(object sender, RoutedEventArgs e) => await RepeatQuizAsync();

    private async Task AskQuizAsync()
    {
        CancelQuizNext();
        var question = EarQuiz.Next(LearningCatalog.GetItems(_quizAlphabet), _quizTarget?.Symbol);
        _quizTarget = question.Target;
        var buttons = QuizAnswerButtons;
        for (var index = 0; index < buttons.Length; index++)
        {
            var answer = index < question.Answers.Count ? question.Answers[index] : null;
            ClearQuizMark(buttons[index]);
            buttons[index].IsEnabled = answer is not null;
            buttons[index].Content = answer?.Symbol.ToString() ?? string.Empty;
            buttons[index].Tag = answer?.Symbol;
        }

        _quizAnswered = false;
        QuizRepeatButton.IsEnabled = true;
        await PlayQuizTargetAsync();
    }

    /// <summary>Повтор сигнала; после ответа (послушать верный символ) следующий ждёт конца повтора.</summary>
    private async Task RepeatQuizAsync()
    {
        if (_quizTarget is null)
        {
            return;
        }

        var waitingForNext = CancelQuizNext();
        await PlayQuizTargetAsync();
        if (waitingForNext && _quizAnswered)
        {
            await ScheduleQuizNextAsync(correct: true);
        }
    }

    private async Task PlayQuizTargetAsync()
    {
        if (_quizTarget is null)
        {
            return;
        }

        StopPlayback();
        StopLearningPlayback();
        if (!_quizAnswered)
        {
            QuizStatusText.Text = Texts.T("Слушайте…");
            QuizStatusText.ClearValue(TextBlock.ForegroundProperty);
        }

        var cancellation = new CancellationTokenSource();
        _learningCancellation = cancellation;
        try
        {
            // Скорость сигнала своя, по ползунку «На слух»; тон и громкость — из параметров тренировки
            var clip = MorseAudioService.Render(_quizTarget.Symbol.ToString(), _quizSpeed, (int)FrequencySlider.Value,
                (int)VolumeSlider.Value, 3, 7);
            _learningAudioStream = new MemoryStream(clip.WavBytes, writable: false);
            _learningPlayer = new SoundPlayer(_learningAudioStream);
            _learningPlayer.Load();
            _learningPlayer.Play();
            await Task.Delay(clip.Duration, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or IOException or System.ComponentModel.Win32Exception)
        {
            // Нет звукового устройства или файл не проигрывается — вопрос остаётся, отвечать можно
            QuizStatusText.Text = Texts.T("Не удалось воспроизвести");
            return;
        }
        finally
        {
            if (ReferenceEquals(_learningCancellation, cancellation))
            {
                _learningCancellation = null;
            }
        }

        if (!_quizAnswered)
        {
            QuizStatusText.Text = Texts.T("Какой символ прозвучал?");
        }
    }

    private void QuizAnswerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            AnswerQuiz(button);
        }
    }

    /// <summary>Кнопка варианта для набранного символа: сам символ или символ с тем же кодом (другая раскладка).</summary>
    internal Button? FindQuizAnswerButton(char typed)
    {
        var symbol = char.ToUpperInvariant(typed) == 'Ё' ? 'Е' : char.ToUpperInvariant(typed);
        var buttons = QuizAnswerButtons.Where(button => button.Tag is char).ToArray();
        return buttons.FirstOrDefault(button => (char)button.Tag == symbol)
               ?? (MorseAlphabet.TryGetCode(symbol, out var code)
                   ? buttons.FirstOrDefault(button => MorseAlphabet.TryGetCode((char)button.Tag, out var answerCode) && answerCode == code)
                   : null);
    }

    private async void AnswerQuiz(Button chosen)
    {
        // Кнопки не выключаются после ответа: у выключенной кнопки не видно подсветки верного ответа
        if (_quizAnswered || _quizTarget is null || chosen.Tag is not char answer)
        {
            return;
        }

        _quizAnswered = true;
        _quizTotal++;
        var correct = answer == _quizTarget.Symbol;
        if (correct)
        {
            _quizCorrect++;
            QuizStatusText.Text = Texts.F("Верно: {0} — {1}", _quizTarget.Symbol, _quizTarget.Chant);
            QuizStatusText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
        }
        else
        {
            QuizStatusText.Text = Texts.F("Правильно: {0} — {1}", _quizTarget.Symbol, _quizTarget.Chant);
            QuizStatusText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            chosen.SetResourceReference(BackgroundProperty, "DangerBrush");
            chosen.Foreground = QuizMarkTextBrush;
        }

        foreach (var button in QuizAnswerButtons.Where(button => button.Tag is char symbol && symbol == _quizTarget.Symbol))
        {
            button.SetResourceReference(BackgroundProperty, "PrimaryBrush");
            button.Foreground = QuizMarkTextBrush;
        }

        MarkPracticed();
        UpdateQuizScore();
        SaveSettingsIfLoaded();
        await ScheduleQuizNextAsync(correct);
    }

    /// <summary>Следующий символ после паузы, если включён автопереход и вкладка «На слух» открыта.</summary>
    private async Task ScheduleQuizNextAsync(bool correct)
    {
        if (QuizAutoNextCheckBox.IsChecked != true)
        {
            return;
        }

        CancelQuizNext();
        var cancellation = new CancellationTokenSource();
        _quizNextCancellation = cancellation;
        try
        {
            await Task.Delay(EarQuiz.NextDelay(correct), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Ушли с вкладки, выключили автопереход или закрыли окно — следующий символ не звучит
        if (!ReferenceEquals(_quizNextCancellation, cancellation) || MainTabs.SelectedIndex != QuizTabIndex
            || QuizAutoNextCheckBox.IsChecked != true || !IsVisible)
        {
            return;
        }

        _quizNextCancellation = null;
        cancellation.Dispose();
        await AskQuizAsync();
    }

    /// <summary>Отменяет ожидающий следующий символ; true — он действительно ждал.</summary>
    private bool CancelQuizNext()
    {
        var pending = _quizNextCancellation;
        if (pending is null)
        {
            return false;
        }

        _quizNextCancellation = null;
        pending.Cancel();
        pending.Dispose();
        return true;
    }

    private void QuizSpeedSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _quizSpeed = EarQuiz.ClampSpeed((int)Math.Round(e.NewValue));
        UpdateQuizSpeedText();
    }

    private void UpdateQuizSpeedText()
    {
        if (QuizSpeedValueText is not null)
        {
            QuizSpeedValueText.Text = Texts.F("{0} знаков/мин", _quizSpeed);
        }
    }

    private void QuizAutoNext_OnChanged(object sender, RoutedEventArgs e)
    {
        // Сохраняется вместе с остальными настройками (ответ, закрытие окна): здесь не пишем, чтобы не сохранить
        // наполовину применённые настройки, когда флажок ставит ApplySettings
        if (QuizAutoNextCheckBox.IsChecked != true)
        {
            CancelQuizNext();
        }
    }

    private void QuizResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _quizCorrect = 0;
        _quizTotal = 0;
        UpdateQuizScore();
        SaveSettingsIfLoaded();
    }

    private void UpdateQuizScore()
    {
        QuizScoreText.Text = Texts.F("Результат: {0} / {1}", _quizCorrect, _quizTotal);
    }

    private static void ClearQuizMark(Button button)
    {
        button.ClearValue(BackgroundProperty);
        button.ClearValue(ForegroundProperty);
    }

    private void StopLearningPlayback()
    {
        _learningCancellation?.Cancel();
        _learningCancellation = null;
        _learningPlayer?.Stop();
        _learningPlayer?.Dispose();
        _learningPlayer = null;
        _learningAudioStream?.Dispose();
        _learningAudioStream = null;
    }

    private void ContentModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCustomSymbolsVisibility();

    // ---------- Что тренировать: четыре основных режима сверху, остальное — в дополнительных ----------

    private static readonly ContentMode[] MainModes = { ContentMode.Letters, ContentMode.Digits, ContentMode.LettersAndDigits, ContentMode.Custom };

    private void ModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var mode))
        {
            return;
        }

        // Полный список режимов в дополнительных настройках — единственный источник режима
        ContentModeCombo.SelectedIndex = mode;
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
        }
    }

    private void UpdateModeButtons()
    {
        // Во время InitializeComponent часть элементов ещё не создана
        if (ModeLettersButton is null || ModeHintText is null || ContentModeCombo is null || AlphabetCombo is null || KochLevelSlider is null)
        {
            return;
        }

        var mode = ContentModes.Clamp(ContentModeCombo.SelectedIndex);
        var buttons = new[] { ModeLettersButton, ModeDigitsButton, ModeBothButton, ModeCustomButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            buttons[index].Style = (Style)FindResource(MainModes[index] == mode ? "PrimaryButton" : typeof(Button));
        }

        var alphabet = (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2);
        ModeHintText.Text = Array.IndexOf(MainModes, mode) < 0
            ? Texts.F("Сейчас особый режим «{0}» — он выбран в дополнительных настройках.", (ContentModeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty)
            : Texts.F("Символов в задании: {0} · алфавит: {1} (меняется в дополнительных настройках)",
                MorseAlphabet.BuildPool(alphabet, mode, _selectedSymbols, (int)KochLevelSlider.Value).Count,
                (AlphabetCombo.SelectedItem as ComboBoxItem)?.Content as string ?? string.Empty);
    }

    private void AdvancedButton_OnClick(object sender, RoutedEventArgs e)
    {
        var show = AdvancedPanel.Visibility != Visibility.Visible;
        AdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        AdvancedButton.Content = show ? Texts.T("Дополнительные настройки ▴") : Texts.T("Дополнительные настройки ▾");
    }

    private void UpdateCustomSymbolsVisibility()
    {
        UpdateModeButtons();
        if (CustomSymbolsPanel is not null && ContentModeCombo is not null)
        {
            CustomSymbolsPanel.Visibility = ContentModeCombo.SelectedIndex == (int)ContentMode.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (KochPanel is not null && ContentModeCombo is not null)
        {
            KochPanel.Visibility = ContentModeCombo.SelectedIndex == (int)ContentMode.Koch
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (GroupHintText is not null && ContentModeCombo is not null)
        {
            GroupHintText.Text = ContentModes.IsWordMode(ContentModes.Clamp(ContentModeCombo.SelectedIndex))
                ? Texts.T("Каждая группа — одно слово, позывной или код")
                : Texts.T("В каждой группе 5 символов");
        }
    }

    private void SettingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_windowLoaded)
        {
            UpdateSettingLabels();
        }
    }

    private void UpdateSettingLabels()
    {
        if (SpeedValueText is null
            || FrequencyValueText is null
            || VolumeValueText is null
            || CharacterGapValueText is null
            || GroupGapValueText is null
            || StartPauseValueText is null
            || SpeedSlider is null
            || FrequencySlider is null
            || VolumeSlider is null
            || CharacterGapSlider is null
            || GroupGapSlider is null
            || StartPauseSlider is null
            || NoiseSlider is null
            || QsbSlider is null
            || DriftSlider is null
            || NoiseValueText is null
            || QsbValueText is null
            || DriftValueText is null)
        {
            return;
        }

        NoiseValueText.Text = $"{(int)NoiseSlider.Value}%";
        QsbValueText.Text = $"{(int)QsbSlider.Value}%";
        DriftValueText.Text = Texts.F("±{0} Гц", (int)DriftSlider.Value);
        SpeedValueText.Text = Texts.F("{0} знаков/мин", (int)SpeedSlider.Value);
        FrequencyValueText.Text = Texts.F("{0} Гц", (int)FrequencySlider.Value);
        VolumeValueText.Text = $"{(int)VolumeSlider.Value}%";
        CharacterGapValueText.Text = Texts.F("{0} точек", (int)CharacterGapSlider.Value);
        GroupGapValueText.Text = Texts.F("{0} точек", (int)GroupGapSlider.Value);
        StartPauseValueText.Text = Texts.F("{0} точек", (int)StartPauseSlider.Value);
    }

    private bool TryReadGroupCount(out int groupCount)
    {
        if (!int.TryParse(GroupCountText.Text, out groupCount) || groupCount is < 1 or > 100)
        {
            MessageBox.Show(Texts.T("Количество групп должно быть целым числом от 1 до 100."), Texts.T("Проверьте параметры"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            GroupCountText.Focus();
            GroupCountText.SelectAll();
            return false;
        }

        return true;
    }

    private AppSettings ReadSettings()
    {
        var groupCount = int.TryParse(GroupCountText.Text, out var parsedGroupCount) ? Math.Clamp(parsedGroupCount, 1, 100) : 10;
        return new AppSettings
        {
            AlphabetIndex = Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2),
            ContentModeIndex = (int)ContentModes.Clamp(ContentModeCombo.SelectedIndex),
            KochLevel = (int)KochLevelSlider.Value,
            EmphasizeProblemSymbols = EmphasizeProblemsCheckBox.IsChecked == true,
            AutoSpeed = AutoSpeedCheckBox.IsChecked == true,
            ExamPlaybacks = ExamSession.ClampPlaybacks(ExamPlaybacksCombo.SelectedIndex + 1),
            ExamTimeLimitMinutes = ExamSession.TimeLimitChoices[Math.Clamp(ExamLimitCombo.SelectedIndex, 0, ExamSession.TimeLimitChoices.Count - 1)],
            DailyGoalMinutes = SelectedDailyGoal,
            ReminderEnabled = ReminderCheckBox.IsChecked == true,
            ReminderMinutes = SelectedReminderMinutes,
            GroupCount = groupCount,
            CharactersPerMinute = (int)SpeedSlider.Value,
            FrequencyHz = (int)FrequencySlider.Value,
            VolumePercent = (int)VolumeSlider.Value,
            CharacterGapUnits = (int)CharacterGapSlider.Value,
            GroupGapUnits = (int)GroupGapSlider.Value,
            StartPauseUnits = (int)StartPauseSlider.Value,
            PlayStartSignal = PlayStartSignalCheckBox.IsChecked == true,
            NoisePercent = (int)NoiseSlider.Value,
            QsbPercent = (int)QsbSlider.Value,
            DriftHz = (int)DriftSlider.Value,
            CustomSymbols = _selectedSymbols,
            ThemeIndex = Math.Clamp(ThemeCombo.SelectedIndex, 0, 2),
            LanguageIndex = Math.Clamp(LanguageCombo.SelectedIndex, 0, 2),
            LearningAlphabetIndex = _learningAlphabet,
            LearningAudioModeIndex = _learningAudioMode,
            QuizCorrect = _quizCorrect,
            QuizTotal = _quizTotal,
            QuizAlphabetIndex = _quizAlphabet,
            QuizSpeed = _quizSpeed,
            QuizAutoNext = QuizAutoNextCheckBox.IsChecked == true,
            ShowAnswerInput = _answerInputPreferred,
            LastPracticeAt = _lastPracticeAt,
            ActiveProfileName = string.IsNullOrWhiteSpace(ProfileCombo.Text) ? "Основной" : ProfileCombo.Text.Trim(),
            WindowWidth = WindowState == WindowState.Normal ? ActualWidth : RestoreBounds.Width,
            WindowHeight = WindowState == WindowState.Normal ? ActualHeight : RestoreBounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
            MainTabIndex = Math.Max(0, MainTabs.SelectedIndex),
            TrainingPanelCollapsed = TrainingPanel.Visibility != Visibility.Visible,
            CourseStep = _courseStep
        };
    }

    private void ApplySettings(AppSettings settings)
    {
        AlphabetCombo.SelectedIndex = Math.Clamp(settings.AlphabetIndex, 0, 2);
        ContentModeCombo.SelectedIndex = (int)ContentModes.Clamp(settings.ContentModeIndex);
        KochLevelSlider.Value = KochMethod.ClampLevel((AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2), settings.KochLevel);
        EmphasizeProblemsCheckBox.IsChecked = settings.EmphasizeProblemSymbols;
        AutoSpeedCheckBox.IsChecked = settings.AutoSpeed;
        ExamPlaybacksCombo.SelectedIndex = ExamSession.ClampPlaybacks(settings.ExamPlaybacks) - 1;
        ExamLimitCombo.SelectedIndex = Math.Max(0, ExamSession.TimeLimitChoices.ToList().IndexOf(ExamSession.ClampTimeLimit(settings.ExamTimeLimitMinutes)));
        DailyGoalCombo.SelectedIndex = Math.Max(0, TrainingStatistics.DailyGoalChoices.ToList().IndexOf(TrainingStatistics.ClampGoal(settings.DailyGoalMinutes)));
        ReminderCheckBox.IsChecked = settings.ReminderEnabled;
        ReminderTimeCombo.SelectedIndex = PracticeNudge.NearestChoiceIndex(settings.ReminderMinutes);
        GroupCountText.Text = Math.Clamp(settings.GroupCount, 1, 100).ToString(CultureInfo.InvariantCulture);
        SpeedSlider.Value = Math.Clamp(settings.CharactersPerMinute, 20, 300);
        FrequencySlider.Value = Math.Clamp(settings.FrequencyHz, 300, 1_200);
        VolumeSlider.Value = Math.Clamp(settings.VolumePercent, 0, 100);
        CharacterGapSlider.Value = Math.Clamp(settings.CharacterGapUnits, 3, 20);
        GroupGapSlider.Value = Math.Clamp(settings.GroupGapUnits, 7, 30);
        StartPauseSlider.Value = Math.Clamp(settings.StartPauseUnits, 7, 60);
        PlayStartSignalCheckBox.IsChecked = settings.PlayStartSignal;
        NoiseSlider.Value = Math.Clamp(settings.NoisePercent, 0, 100);
        QsbSlider.Value = Math.Clamp(settings.QsbPercent, 0, 100);
        DriftSlider.Value = Math.Clamp(settings.DriftHz, 0, NoiseProfile.MaxDriftHz);
        _selectedSymbols = string.IsNullOrWhiteSpace(settings.CustomSymbols) ? "АГЖД" : settings.CustomSymbols;
        ThemeCombo.SelectedIndex = Math.Clamp(settings.ThemeIndex, 0, 2);
        LanguageCombo.SelectedIndex = Math.Clamp(settings.LanguageIndex, 0, 2);
        _learningAlphabet = Math.Clamp(settings.LearningAlphabetIndex, 0, 3);
        HighlightChoice(LearningAlphabetButtons, _learningAlphabet);
        _learningAudioMode = Math.Clamp(settings.LearningAudioModeIndex, 0, 1);
        UpdateLearningAudioModeButtons();
        _quizAlphabet = Math.Clamp(settings.QuizAlphabetIndex, 0, 3);
        HighlightChoice(QuizAlphabetButtons, _quizAlphabet);
        _quizSpeed = EarQuiz.ClampSpeed(settings.QuizSpeed);
        QuizSpeedSlider.Value = _quizSpeed;
        UpdateQuizSpeedText();
        QuizAutoNextCheckBox.IsChecked = settings.QuizAutoNext;
        _lastPracticeAt = settings.LastPracticeAt;
        _courseStep = Math.Max(0, settings.CourseStep);
        UpdateKochSummary();
    }

    private void LoadProfiles(AppSettings settings)
    {
        _profiles = _profileService.Load();
        if (_profiles.Count == 0)
        {
            _profiles = _profileService.Save(TrainingProfile.FromSettings("Основной", settings));
        }

        _applyingProfile = true;
        ProfileCombo.ItemsSource = _profiles;
        ProfileCombo.DisplayMemberPath = nameof(TrainingProfile.Name);
        ProfileCombo.SelectedItem = _profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase)) ?? _profiles[0];
        ProfileCombo.Text = ((TrainingProfile)ProfileCombo.SelectedItem).Name;
        _applyingProfile = false;
    }

    private void ProfileCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowLoaded || _applyingProfile || ProfileCombo.SelectedItem is not TrainingProfile profile)
        {
            return;
        }

        var settings = ReadSettings();
        profile.ApplyTo(settings);
        ApplySettings(settings);
        UpdateSettingLabels();
        UpdateCustomSymbolsVisibility();
        UpdateSelectedSymbolsSummary();
        ProfileCombo.Text = profile.Name;
        _settingsService.Save(settings);
    }

    private void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = TrainingProfile.NormalizeName(ProfileCombo.Text);
            var settings = ReadSettings();
            settings.ActiveProfileName = name;
            _profiles = _profileService.Save(TrainingProfile.FromSettings(name, settings));
            LoadProfiles(settings);
            _settingsService.Save(settings);
            SetStatus(Texts.T("ПРОФИЛЬ СОХРАНЁН"), isActive: true);
        }
        catch (ArgumentException)
        {
            MessageBox.Show(Texts.T("Введите название профиля."), Texts.T("Профили"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportProfilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Экспорт профилей"),
            Filter = Texts.T("Профили Morse Trainer (*.json)|*.json"),
            FileName = $"morse-profiles-{DateTime.Now:yyyy-MM-dd}.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // Свои напевы едут вместе с профилями
            File.WriteAllText(dialog.FileName, ProfileTransfer.Export(_profiles, LearningCatalog.CustomChants), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SetStatus(Texts.T("ПРОФИЛИ ЭКСПОРТИРОВАНЫ"), isActive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Экспорт профилей"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportProfilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Texts.T("Импорт профилей"),
            Filter = Texts.T("Профили Morse Trainer (*.json)|*.json|Все файлы (*.*)|*.*")
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var package = ProfileTransfer.ImportPackage(File.ReadAllText(dialog.FileName));
            _profiles = _profileService.Import(package.Profiles);
            LoadProfiles(ReadSettings());
            var message = Texts.F("Импортировано профилей: {0}.", package.Profiles.Count);
            if (package.Chants.Count > 0)
            {
                LearningCatalog.SetCustomChants(_chantStore.Merge(package.Chants));
                RefreshLearningItems();
                UpdateCustomVoiceText();
                message += " " + Texts.F("Своих напевов: {0}.", package.Chants.Count);
            }

            MessageBox.Show(this, message, Texts.T("Профили"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Импорт профилей"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not TrainingProfile profile)
        {
            return;
        }

        _profiles = _profileService.Delete(profile.Name);
        var settings = ReadSettings();
        if (_profiles.Count == 0)
        {
            settings.ActiveProfileName = "Основной";
            _profiles = _profileService.Save(TrainingProfile.FromSettings("Основной", settings));
        }
        else
        {
            settings.ActiveProfileName = _profiles[0].Name;
            _profiles[0].ApplyTo(settings);
            ApplySettings(settings);
        }

        LoadProfiles(settings);
        _settingsService.Save(settings);
    }

    private void SetTaskControlsEnabled(bool enabled)
    {
        PlayButton.IsEnabled = enabled;
        RepeatButton.IsEnabled = enabled;
        StopButton.IsEnabled = false;
        ToggleAnswerButton.IsEnabled = enabled;
        UserAnswerText.IsEnabled = enabled;
        CheckAnswerButton.IsEnabled = enabled;
        SaveTaskButton.IsEnabled = enabled;
        ExportWavButton.IsEnabled = enabled;
    }

    private void SetGenerationState(bool isGenerating)
    {
        GenerateButton.IsEnabled = !isGenerating;
        GenerateButton.Content = isGenerating ? Texts.T("Подготовка звука…") : Texts.T("Сгенерировать задание");
        if (isGenerating)
        {
            SetTaskControlsEnabled(false);
            SetStatus(Texts.T("ГЕНЕРАЦИЯ"), isActive: true);
            PlaybackStatusText.Text = Texts.T("Формируем случайные группы и звук…");
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

/// <summary>Строка таблицы истории на вкладке «Прогресс».</summary>
public sealed record HistoryRow(string When, string Profile, string Speed, string Groups, string Accuracy, string Errors);
