using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer;

public partial class MainWindow : Window
{
    private static readonly HttpClient UpdateClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SettingsService _settingsService = new();
    private readonly TrainingHistoryStore _historyStore = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorseTrainer", "history.json"));
    private IReadOnlyList<TrainingRecord> _history = Array.Empty<TrainingRecord>();
    private bool _currentTaskRecorded;
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
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        SubtitleText.Text = $"Тренировка и изучение азбуки Морзе · версия {CurrentVersion}";
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
        StopPlayback(resetProgress: false);
        StopLearningPlayback();
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
        else if (e.Key == Key.Escape)
        {
            StopPlayback();
            StopLearningPlayback();
            e.Handled = true;
        }
    }

    private static Version CurrentVersion =>
        UpdateService.Normalize(typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0));

    // Единственное место, где приложение выходит в интернет, и только по нажатию кнопки
    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Проверяю…";
        try
        {
            var info = await UpdateService.FetchLatestAsync(UpdateClient, CancellationToken.None);
            if (UpdateService.IsNewer(CurrentVersion, info.LatestVersion))
            {
                var notes = string.IsNullOrWhiteSpace(info.Notes) ? string.Empty : "\n\n" + Truncate(info.Notes, 600);
                var answer = MessageBox.Show(this,
                    $"Доступна версия {info.LatestVersion}, у вас {CurrentVersion}.{notes}\n\nОткрыть страницу загрузки?",
                    "Обновление Morse Trainer", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer == MessageBoxResult.Yes)
                {
                    OpenInBrowser(info.WindowsInstallerUrl ?? info.ReleasePageUrl);
                }
            }
            else
            {
                MessageBox.Show(this, $"У вас последняя версия {CurrentVersion}.", "Обновление Morse Trainer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this,
                $"Не удалось проверить обновления. Проверьте подключение к интернету.\n\n{exception.Message}",
                "Обновление Morse Trainer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            CheckUpdatesButton.Content = "Обновления";
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static void OpenInBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void RefreshProgress()
    {
        var summary = TrainingStatistics.Summarize(_history);
        ProgressSessionsText.Text = summary.Sessions.ToString(CultureInfo.InvariantCulture);
        ProgressAverageText.Text = summary.Sessions == 0 ? "—" : $"{summary.AverageAccuracy:0.#}%";
        ProgressBestText.Text = summary.Sessions == 0 ? "—" : $"{summary.BestAccuracy:0.#}%";
        ProgressSymbolsText.Text = summary.Sessions == 0 ? "—" : $"{summary.CorrectSymbols} / {summary.TotalSymbols}";
        var problems = TrainingStatistics.ProblemSymbols(_history);
        HistoryProblemSymbolsText.Text = problems.Count == 0
            ? "—"
            : string.Join("  ", problems.Select(item => $"{item.Symbol} ×{item.Count}"));
        HistoryListView.ItemsSource = _history
            .OrderByDescending(item => item.CompletedAt)
            .Take(50)
            .Select(item => new HistoryRow(
                item.CompletedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture),
                item.ProfileName,
                $"{item.CharactersPerMinute} зн/мин",
                item.GroupCount.ToString(CultureInfo.InvariantCulture),
                $"{item.AccuracyPercent:0.#}%",
                item.ProblemSymbols.Length == 0 ? "—" : string.Join(" ", item.ProblemSymbols.Distinct())))
            .ToList();

        DailyBarsPanel.Children.Clear();
        var days = TrainingStatistics.ByDay(_history);
        if (days.Count == 0)
        {
            DailyBarsPanel.Children.Add(new TextBlock { Text = "Пока пусто", Foreground = (Brush)FindResource("MutedTextBrush") });
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
        var answer = MessageBox.Show(this, "Удалить все записи о тренировках на этом компьютере?", "Очистить историю",
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

    private async Task GenerateTaskAsync()
    {
        if (!TryReadGroupCount(out var groupCount))
        {
            return;
        }

        var settings = ReadSettings();
        settings.GroupCount = groupCount;
        var alphabet = (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2);
        var content = (ContentMode)Math.Clamp(ContentModeCombo.SelectedIndex, 0, 5);
        var pool = MorseAlphabet.BuildPool(alphabet, content, _selectedSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            MessageBox.Show(
                "В выбранном наборе нет символов. Откройте выбор и отметьте хотя бы одну букву или цифру.",
                "Не удалось создать задание",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        StopPlayback();
        SetGenerationState(isGenerating: true);

        try
        {
            // Чаще звучат новый символ метода Коха и символы с ошибками из истории
            var emphasized = new HashSet<char>();
            if (content == ContentMode.Koch)
            {
                emphasized.Add(pool[^1]);
            }

            if (settings.EmphasizeProblemSymbols)
            {
                foreach (var problem in TrainingStatistics.ProblemSymbols(_history, 6))
                {
                    if (pool.Contains(problem.Symbol))
                    {
                        emphasized.Add(problem.Symbol);
                    }
                }
            }

            var generatedTask = TrainingGenerator.Generate(pool, settings.GroupCount, emphasized);
            var audio = await Task.Run(() => MorseAudioService.Render(
                generatedTask,
                settings.CharactersPerMinute,
                settings.FrequencyHz,
                settings.VolumePercent,
                settings.CharacterGapUnits,
                settings.GroupGapUnits,
                settings.PlayStartSignal,
                settings.StartPauseUnits));

            _currentTask = generatedTask;
            _currentTaskRecorded = false;
            _currentClip = audio;
            _currentSettings = settings;
            _answerVisible = false;
            UserAnswerText.Clear();
            ResultDetailsText.Text = "Пробелы между группами при проверке не учитываются";
            ResultDetailsText.Foreground = (Brush)FindResource("MutedTextBrush");
            AccuracyText.Text = "—";
            PlaybackProgress.Value = 0;
            PlaybackStatusText.Text = $"Готово к воспроизведению · {FormatDuration(audio.Duration)}";
            var startSignal = settings.PlayStartSignal ? " · старт Ж Ж Ж" : string.Empty;
            TaskMetaText.Text = $"{settings.GroupCount} групп × 5 · {settings.CharactersPerMinute} знаков/мин · паузы {settings.CharacterGapUnits}/{settings.GroupGapUnits}{startSignal}";
            UpdateAnswerDisplay();
            SetStatus("ГОТОВО", isActive: true);
            SetTaskControlsEnabled(true);
            _settingsService.Save(settings);
        }
        catch (Exception exception)
        {
            _currentTask = string.Empty;
            _currentClip = null;
            _currentSettings = null;
            SetTaskControlsEnabled(false);
            SetStatus("ОШИБКА", isActive: false);
            PlaybackStatusText.Text = "Не удалось подготовить звук";
            MessageBox.Show($"Не удалось создать задание.\n\n{exception.Message}", "Morse Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private async Task PlayCurrentAsync()
    {
        if (_currentClip is null)
        {
            return;
        }

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
            PlaybackStatusText.Text = "Идёт воспроизведение…";
            SetStatus("СЛУШАЕМ", isActive: true);

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
                PlaybackStatusText.Text = "Прослушивание завершено — введите ответ";
                SetStatus("ВАШ ОТВЕТ", isActive: true);
                UserAnswerText.Focus();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Не удалось воспроизвести звук.\n\n{exception.Message}", "Morse Trainer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                PlayButton.IsEnabled = _currentClip is not null;
                RepeatButton.IsEnabled = _currentClip is not null;
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
                PlaybackStatusText.Text = $"Готово к воспроизведению · {FormatDuration(_currentClip.Duration)}";
                SetStatus("ГОТОВО", isActive: true);
            }
        }

        if (PlayButton is not null)
        {
            PlayButton.IsEnabled = _currentClip is not null;
            RepeatButton.IsEnabled = _currentClip is not null;
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
            ToggleAnswerButton.Content = "Показать";
            return;
        }

        AnswerDisplayText.Text = _answerVisible
            ? _currentTask
            : new string(_currentTask.Select(symbol => char.IsWhiteSpace(symbol) ? ' ' : '\u2022').ToArray());
        ToggleAnswerButton.Content = _answerVisible ? "Скрыть" : "Показать";
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
        // В историю попадает только первая проверка каждого задания
        if (!_currentTaskRecorded)
        {
            _currentTaskRecorded = true;
            var taskSettings = _currentSettings ?? ReadSettings();
            var record = TrainingStatistics.CreateRecord(DateTime.Now, taskSettings.ActiveProfileName,
                taskSettings.CharactersPerMinute, _currentTask.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, result);
            _history = _historyStore.Add(record);
            RefreshProgress();
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
            ResultDetailsText.Text = "Отлично: все символы распознаны правильно";
            ResultDetailsText.Foreground = (Brush)FindResource("PrimaryBrush");
            SetStatus("БЕЗ ОШИБОК", isActive: true);
        }
        else
        {
            var details = result.Mistakes.Take(8)
                .Select(mistake => $"{mistake.Position}: {mistake.Expected}→{mistake.Actual?.ToString() ?? "∅"}");
            var suffix = result.Mistakes.Count > 8 ? " …" : string.Empty;
            ResultDetailsText.Text = $"Ошибки: {string.Join(", ", details)}{suffix}";
            ResultDetailsText.Foreground = (Brush)FindResource("DangerBrush");
            SetStatus("ЕСТЬ ОШИБКИ", isActive: false);
        }

        if (_currentSettings?.ContentModeIndex == (int)ContentMode.Koch)
        {
            var kochAlphabet = (AlphabetMode)Math.Clamp(_currentSettings.AlphabetIndex, 0, 2);
            ResultDetailsText.Text += "\n" + KochMethod.Advice(kochAlphabet, _currentSettings.KochLevel, result.AccuracyPercent);
        }

        _answerVisible = true;
        UpdateAnswerDisplay();
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
            ? "Ничего не выбрано"
            : $"{_selectedSymbols.Length} символов: {Truncate(_selectedSymbols, 45)}";
    }

    private void SaveTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить задание",
            Filter = "Текстовый файл (*.txt)|*.txt",
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
            .AppendLine($"Создано: {DateTime.Now:dd.MM.yyyy HH:mm}")
            .AppendLine($"Групп: {settings.GroupCount} × 5")
            .AppendLine($"Скорость: {settings.CharactersPerMinute} знаков/мин")
            .AppendLine($"Тональность: {settings.FrequencyHz} Гц")
            .AppendLine($"Паузы: символы {settings.CharacterGapUnits}, группы {settings.GroupGapUnits}")
            .AppendLine(settings.PlayStartSignal
                ? $"Старт: Ж Ж Ж, затем пауза {settings.StartPauseUnits} точек"
                : "Стартовый сигнал: выключен")
            .AppendLine().AppendLine("Задание:").AppendLine(_currentTask).ToString();
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
            Title = "Экспортировать звук",
            Filter = "Звуковой файл WAV (*.wav)|*.wav",
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
                LearningPlaybackStatusText.Text = "Произносим напев…";
                _learningAudioStream = VoicePackService.Open(item.Symbol);
                if (_learningAudioStream is null)
                {
                    LearningPlaybackStatusText.Text = "Офлайн-напев недоступен — воспроизводим сигнал";
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
            LearningPlaybackStatusText.Text = "Слушаем ритм символа…";

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
            LearningPlaybackStatusText.Text = "Готово — можно прослушать ещё раз";
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
            QuizResultText.Text = "Для проверки нужно не менее четырёх символов.";
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
        QuizPromptText.Text = "Слушайте…";
        await PlayQuizSignalAsync(_quizTarget);
        QuizPromptText.Text = "Какой символ прозвучал?";
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
            QuizResultText.Text = $"Верно: {_quizTarget.Symbol} — {_quizTarget.Chant}";
            QuizResultText.Foreground = (Brush)FindResource("PrimaryBrush");
        }
        else
        {
            QuizResultText.Text = $"Правильный ответ: {_quizTarget.Symbol} — {_quizTarget.Chant}";
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
            || StartPauseSlider is null)
        {
            return;
        }

        SpeedValueText.Text = $"{(int)SpeedSlider.Value} знаков/мин";
        FrequencyValueText.Text = $"{(int)FrequencySlider.Value} Гц";
        VolumeValueText.Text = $"{(int)VolumeSlider.Value}%";
        CharacterGapValueText.Text = $"{(int)CharacterGapSlider.Value} точек";
        GroupGapValueText.Text = $"{(int)GroupGapSlider.Value} точек";
        StartPauseValueText.Text = $"{(int)StartPauseSlider.Value} точек";
    }

    private bool TryReadGroupCount(out int groupCount)
    {
        if (!int.TryParse(GroupCountText.Text, out groupCount) || groupCount is < 1 or > 100)
        {
            MessageBox.Show("Количество групп должно быть целым числом от 1 до 100.", "Проверьте параметры",
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
            ContentModeIndex = Math.Clamp(ContentModeCombo.SelectedIndex, 0, 5),
            KochLevel = (int)KochLevelSlider.Value,
            EmphasizeProblemSymbols = EmphasizeProblemsCheckBox.IsChecked == true,
            GroupCount = groupCount,
            CharactersPerMinute = (int)SpeedSlider.Value,
            FrequencyHz = (int)FrequencySlider.Value,
            VolumePercent = (int)VolumeSlider.Value,
            CharacterGapUnits = (int)CharacterGapSlider.Value,
            GroupGapUnits = (int)GroupGapSlider.Value,
            StartPauseUnits = (int)StartPauseSlider.Value,
            PlayStartSignal = PlayStartSignalCheckBox.IsChecked == true,
            CustomSymbols = _selectedSymbols,
            ThemeIndex = Math.Clamp(ThemeCombo.SelectedIndex, 0, 2),
            LearningAlphabetIndex = Math.Clamp(LearningAlphabetCombo.SelectedIndex, 0, 3),
            LearningAudioModeIndex = Math.Clamp(LearningAudioModeCombo.SelectedIndex, 0, 1),
            QuizCorrect = _quizCorrect,
            QuizTotal = _quizTotal,
            ActiveProfileName = string.IsNullOrWhiteSpace(ProfileCombo.Text) ? "Основной" : ProfileCombo.Text.Trim()
        };
    }

    private void ApplySettings(AppSettings settings)
    {
        AlphabetCombo.SelectedIndex = Math.Clamp(settings.AlphabetIndex, 0, 2);
        ContentModeCombo.SelectedIndex = Math.Clamp(settings.ContentModeIndex, 0, 5);
        KochLevelSlider.Value = KochMethod.ClampLevel((AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2), settings.KochLevel);
        EmphasizeProblemsCheckBox.IsChecked = settings.EmphasizeProblemSymbols;
        GroupCountText.Text = Math.Clamp(settings.GroupCount, 1, 100).ToString(CultureInfo.InvariantCulture);
        SpeedSlider.Value = Math.Clamp(settings.CharactersPerMinute, 20, 300);
        FrequencySlider.Value = Math.Clamp(settings.FrequencyHz, 300, 1_200);
        VolumeSlider.Value = Math.Clamp(settings.VolumePercent, 0, 100);
        CharacterGapSlider.Value = Math.Clamp(settings.CharacterGapUnits, 3, 20);
        GroupGapSlider.Value = Math.Clamp(settings.GroupGapUnits, 7, 30);
        StartPauseSlider.Value = Math.Clamp(settings.StartPauseUnits, 7, 60);
        PlayStartSignalCheckBox.IsChecked = settings.PlayStartSignal;
        _selectedSymbols = string.IsNullOrWhiteSpace(settings.CustomSymbols) ? "АГЖД" : settings.CustomSymbols;
        ThemeCombo.SelectedIndex = Math.Clamp(settings.ThemeIndex, 0, 2);
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
            SetStatus("ПРОФИЛЬ СОХРАНЁН", isActive: true);
        }
        catch (ArgumentException)
        {
            MessageBox.Show("Введите название профиля.", "Профили", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportProfilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт профилей",
            Filter = "Профили Morse Trainer (*.json)|*.json",
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
            SetStatus("ПРОФИЛИ ЭКСПОРТИРОВАНЫ", isActive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Экспорт профилей", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportProfilesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт профилей",
            Filter = "Профили Morse Trainer (*.json)|*.json|Все файлы (*.*)|*.*"
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
            MessageBox.Show(this, $"Импортировано профилей: {imported.Count}.", "Профили", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Импорт профилей", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        GenerateButton.Content = isGenerating ? "Подготовка звука…" : "Сгенерировать задание";
        if (isGenerating)
        {
            SetTaskControlsEnabled(false);
            SetStatus("ГЕНЕРАЦИЯ", isActive: true);
            PlaybackStatusText.Text = "Формируем случайные группы и звук…";
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
        : $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))} сек";

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength
        ? value
        : value[..maxLength] + "…";
}

/// <summary>Строка таблицы истории на вкладке «Прогресс».</summary>
public sealed record HistoryRow(string When, string Profile, string Speed, string Groups, string Accuracy, string Errors);
