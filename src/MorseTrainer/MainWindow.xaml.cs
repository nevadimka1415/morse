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
    private IReadOnlyList<TrainingRecord> _history = Array.Empty<TrainingRecord>();
    private bool _currentTaskRecorded;
    private ExamSession? _exam;
    private DrillPlan? _currentDrill;
    private DispatcherTimer? _examTimer;
    private string? _examReport;
    private string _autoSpeedNote = string.Empty;
    private const int KeyerTabIndex = 2;
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

    public MainWindow()
    {
        InitializeComponent();
        ApplyWindowBounds(_settingsService.Load());
    }

    /// <summary>Размер окна и вкладка из прошлого запуска; применяется до показа, чтобы окно сразу встало по центру нужного размера.</summary>
    private void ApplyWindowBounds(AppSettings settings)
    {
        if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        MainTabs.SelectedIndex = Math.Clamp(settings.MainTabIndex, 0, MainTabs.Items.Count - 1);
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
        RefreshLearningItems();
        _history = _historyStore.Load();
        RefreshProgress();
        _windowLoaded = true;
        await GenerateTaskAsync();
    }

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        _examTimer?.Stop();
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
        HistoryListView.ItemsSource = history
            .OrderByDescending(item => item.CompletedAt)
            .Take(50)
            .Select(item => new HistoryRow(
                item.CompletedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture),
                item.IsExam ? Texts.F("{0} · экзамен", item.ProfileName) : item.ProfileName,
                Texts.F("{0} зн/мин", item.CharactersPerMinute),
                item.GroupCount.ToString(CultureInfo.InvariantCulture),
                $"{item.AccuracyPercent:0.#}%",
                item.ProblemSymbols.Length == 0 ? "—" : string.Join(" ", item.ProblemSymbols.Distinct())))
            .ToList();

        DailyBarsPanel.Children.Clear();
        var days = TrainingStatistics.ByDay(history);
        if (days.Count == 0)
        {
            DailyBarsPanel.Children.Add(new TextBlock { Text = Texts.T("Пока пусто"), Foreground = (Brush)FindResource("MutedTextBrush") });
            return;
        }

        foreach (var day in days)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            var label = new TextBlock { Text = day.Date.ToString("dd.MM", CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border
            {
                Background = (Brush)FindResource("PrimaryBrush"),
                CornerRadius = new CornerRadius(5),
                Height = 12,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 220 * day.AverageAccuracy / 100)
            };
            var track = new Border
            {
                Background = (Brush)FindResource("CardAltBrush"),
                CornerRadius = new CornerRadius(5),
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Child = fill
            };
            var value = new TextBlock
            {
                Text = $"{day.AverageAccuracy:0.#}% · {day.Sessions}",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = (Brush)FindResource("MutedTextBrush")
            };
            Grid.SetColumn(track, 1);
            Grid.SetColumn(value, 2);
            row.Children.Add(label);
            row.Children.Add(track);
            row.Children.Add(value);
            DailyBarsPanel.Children.Add(row);
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

    private void AlphabetCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateKochSummary();

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
        KeyerTargetText.Text = _keyerTarget;
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
                PlaybackStatusText.Text = Texts.T("Прослушивание завершено — введите ответ");
                SetStatus(Texts.T("ВАШ ОТВЕТ"), isActive: true);
                UserAnswerText.Focus();
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

    private void CheckAnswerButton_OnClick(object sender, RoutedEventArgs e)
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
            var record = TrainingStatistics.CreateRecord(DateTime.Now, taskSettings.ActiveProfileName,
                taskSettings.CharactersPerMinute, _currentTask.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, result, examResult is not null);
            _history = _historyStore.Add(record);
            RefreshProgress();
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
            ResultDetailsText.Text = ExamReport.Summary(examResult);
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

    private async void ExamButton_OnClick(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 0;
        await GenerateTaskAsync();
        if (string.IsNullOrEmpty(_currentTask) || _currentSettings is null || _currentClip is null)
        {
            return;
        }

        var groupCount = _currentTask.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        _exam = new ExamSession(_currentTask, _currentSettings.CharactersPerMinute, groupCount, _currentSettings.ActiveProfileName, DateTime.Now);
        _examReport = null;
        _answerVisible = false;
        UpdateAnswerDisplay();
        // Ответ скрыт до проверки, повтор запрещён: прослушивание одно
        ToggleAnswerButton.IsEnabled = false;
        RepeatButton.IsEnabled = false;
        if (_examTimer is null)
        {
            _examTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _examTimer.Tick += ExamTimer_OnTick;
        }

        _examTimer.Start();
        UpdateExamStatus();
        SetStatus(Texts.T("ЭКЗАМЕН"), isActive: true);
        await PlayCurrentAsync();
    }

    private void ExamTimer_OnTick(object? sender, EventArgs e) => UpdateExamStatus();

    private void UpdateExamStatus()
    {
        if (_exam is null || _exam.IsFinished)
        {
            return;
        }

        TaskMetaText.Text = Texts.F("Экзамен · одно прослушивание · {0}", ExamReport.FormatDuration(_exam.Elapsed(DateTime.Now)));
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

    private void LearningFilter_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshLearningItemsIfReady();
    }

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
        var items = LearningCatalog.GetItems(Math.Clamp(LearningAlphabetCombo?.SelectedIndex ?? 0, 0, 3));
        _visibleLearningItems = string.IsNullOrEmpty(search)
            ? items
            : items.Where(item => item.Symbol.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || item.Chant.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        LearningItemsControl.ItemsSource = _visibleLearningItems;
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
            if (LearningAudioModeCombo.SelectedIndex == 1)
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

    private async void QuizPlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        var pool = GetQuizPool();
        if (pool.Count < 4)
        {
            QuizResultText.Text = Texts.T("Для проверки нужно не менее четырёх символов.");
            return;
        }

        _quizTarget = pool[RandomNumberGenerator.GetInt32(pool.Count)];
        var answers = new List<LearningSymbolItem> { _quizTarget };
        var distractors = pool.Where(item => item.Symbol != _quizTarget.Symbol && item.Code != _quizTarget.Code)
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(3);
        answers.AddRange(distractors);
        answers = answers.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();

        QuizAnswersPanel.Children.Clear();
        foreach (var answer in answers)
        {
            var button = new Button
            {
                Content = answer.Symbol.ToString(),
                Tag = answer.Symbol,
                Margin = new Thickness(4),
                FontSize = 20,
                IsEnabled = false
            };
            button.Click += QuizAnswerButton_OnClick;
            QuizAnswersPanel.Children.Add(button);
        }

        QuizResultText.Text = " ";
        QuizPromptText.Text = Texts.T("Слушайте…");
        await PlayQuizSignalAsync(_quizTarget);
        QuizPromptText.Text = Texts.T("Какой символ прозвучал?");
        foreach (var button in QuizAnswersPanel.Children.OfType<Button>())
        {
            button.IsEnabled = true;
        }
    }

    private IReadOnlyList<LearningSymbolItem> GetQuizPool()
    {
        if (LearningAlphabetCombo.SelectedIndex == 2)
        {
            return LearningCatalog.Russian;
        }

        return _visibleLearningItems.Count >= 4
            ? _visibleLearningItems
            : LearningCatalog.GetItems(LearningAlphabetCombo.SelectedIndex);
    }

    private async Task PlayQuizSignalAsync(LearningSymbolItem item)
    {
        StopPlayback();
        StopLearningPlayback();
        var cancellation = new CancellationTokenSource();
        _learningCancellation = cancellation;
        try
        {
            var clip = MorseAudioService.Render(item.Symbol.ToString(), 45, (int)FrequencySlider.Value,
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
        finally
        {
            if (ReferenceEquals(_learningCancellation, cancellation))
            {
                _learningCancellation = null;
            }
        }
    }

    private void QuizAnswerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_quizTarget is null || sender is not Button { Tag: char selected })
        {
            return;
        }

        _quizTotal++;
        var isCorrect = selected == _quizTarget.Symbol;
        if (isCorrect)
        {
            _quizCorrect++;
            QuizResultText.Text = Texts.F("Верно: {0} — {1}", _quizTarget.Symbol, _quizTarget.Chant);
            QuizResultText.Foreground = (Brush)FindResource("PrimaryBrush");
        }
        else
        {
            QuizResultText.Text = Texts.F("Правильный ответ: {0} — {1}", _quizTarget.Symbol, _quizTarget.Chant);
            QuizResultText.Foreground = (Brush)FindResource("DangerBrush");
        }

        foreach (var button in QuizAnswersPanel.Children.OfType<Button>())
        {
            button.IsEnabled = false;
            if (button.Tag is char symbol && symbol == _quizTarget.Symbol)
            {
                button.BorderBrush = (Brush)FindResource("PrimaryBrush");
                button.BorderThickness = new Thickness(2);
            }
        }

        UpdateQuizScore();
        _settingsService.Save(ReadSettings());
    }

    private void UpdateQuizScore()
    {
        QuizScoreText.Text = $"{_quizCorrect} / {_quizTotal}";
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

    private void UpdateCustomSymbolsVisibility()
    {
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
            LearningAlphabetIndex = Math.Clamp(LearningAlphabetCombo.SelectedIndex, 0, 3),
            LearningAudioModeIndex = Math.Clamp(LearningAudioModeCombo.SelectedIndex, 0, 1),
            QuizCorrect = _quizCorrect,
            QuizTotal = _quizTotal,
            ActiveProfileName = string.IsNullOrWhiteSpace(ProfileCombo.Text) ? "Основной" : ProfileCombo.Text.Trim(),
            WindowWidth = WindowState == WindowState.Normal ? ActualWidth : RestoreBounds.Width,
            WindowHeight = WindowState == WindowState.Normal ? ActualHeight : RestoreBounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
            MainTabIndex = Math.Max(0, MainTabs.SelectedIndex)
        };
    }

    private void ApplySettings(AppSettings settings)
    {
        AlphabetCombo.SelectedIndex = Math.Clamp(settings.AlphabetIndex, 0, 2);
        ContentModeCombo.SelectedIndex = (int)ContentModes.Clamp(settings.ContentModeIndex);
        KochLevelSlider.Value = KochMethod.ClampLevel((AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2), settings.KochLevel);
        EmphasizeProblemsCheckBox.IsChecked = settings.EmphasizeProblemSymbols;
        AutoSpeedCheckBox.IsChecked = settings.AutoSpeed;
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
        LearningAlphabetCombo.SelectedIndex = Math.Clamp(settings.LearningAlphabetIndex, 0, 3);
        LearningAudioModeCombo.SelectedIndex = Math.Clamp(settings.LearningAudioModeIndex, 0, 1);
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
            File.WriteAllText(dialog.FileName, ProfileTransfer.Export(_profiles), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
            var imported = ProfileTransfer.Import(File.ReadAllText(dialog.FileName));
            _profiles = _profileService.Import(imported);
            LoadProfiles(ReadSettings());
            MessageBox.Show(this, Texts.F("Импортировано профилей: {0}.", imported.Count), Texts.T("Профили"), MessageBoxButton.OK, MessageBoxImage.Information);
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
