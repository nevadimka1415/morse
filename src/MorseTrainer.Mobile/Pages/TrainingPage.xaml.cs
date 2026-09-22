using Microsoft.Maui.ApplicationModel.DataTransfer;
using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile.Pages;

public partial class TrainingPage : ContentPage
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private readonly TrainingHistoryStore _historyStore;
    private bool _currentTaskRecorded;
    private ExamSession? _exam;
    private string? _examReport;
    private bool _examTimerRunning;
    private int _currentGroupCount;
    private CancellationTokenSource? _playbackCancellation;
    private AudioClip? _currentClip;
    private string _currentTask = string.Empty;
    private bool _answerVisible;

    public TrainingPage(IAudioPlaybackService audioPlayback, MobileSettingsService settingsService, TrainingHistoryStore historyStore)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _historyStore = historyStore;
        _settingsService.SettingsChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshSettingsSummary);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        RefreshSettingsSummary();
        UpdateHistoryLabel();
        if (string.IsNullOrEmpty(_currentTask))
        {
            await GenerateTaskAsync();
        }
    }

    private void RefreshSettingsSummary()
    {
        var settings = _settingsService.LoadSettings();
        ProfileLabel.Text = Texts.F("Профиль: {0}", settings.ActiveProfileName);
        var start = settings.PlayStartSignal ? Texts.F(" · Ж Ж Ж + пауза {0}", settings.StartPauseUnits) : Texts.T(" · без сигнала старта");
        if (settings.ContentModeIndex == (int)ContentMode.Koch)
        {
            start += Texts.F(" · Кох: уровень {0}", settings.KochLevel);
        }

        if (!new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz).IsClean)
        {
            start += Texts.T(" · помехи");
        }

        var groups = ContentModes.IsWordMode(ContentModes.Clamp(settings.ContentModeIndex))
            ? Texts.F("{0} слов · {1} знаков/мин · ", settings.GroupCount, settings.CharactersPerMinute)
            : Texts.F("{0} групп × 5 · {1} знаков/мин · ", settings.GroupCount, settings.CharactersPerMinute);
        SettingsSummaryLabel.Text = groups +
                                    Texts.F("{0} Гц · паузы {1}/{2}{3}", settings.FrequencyHz, settings.CharacterGapUnits, settings.GroupGapUnits, start);
    }

    private async void SettingsButton_OnClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//settings");
    }

    private async void GenerateButton_OnClicked(object sender, EventArgs e)
    {
        await GenerateTaskAsync();
    }

    private async Task GenerateTaskAsync()
    {
        StopPlayback();
        EndExam();
        var settings = _settingsService.LoadSettings();
        var alphabet = (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2);
        var content = ContentModes.Clamp(settings.ContentModeIndex);
        var pool = MorseAlphabet.BuildPool(alphabet, content, settings.CustomSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            await DisplayAlertAsync(Texts.T("Нет символов"), Texts.T("Откройте настройки и выберите хотя бы один символ."), Texts.T("Понятно"));
            return;
        }

        GenerateButton.IsEnabled = false;
        PlaybackStatusLabel.Text = Texts.T("Создаём задание…");
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
                foreach (var problem in TrainingStatistics.ProblemSymbols(_historyStore.Load(), 6))
                {
                    if (pool.Contains(problem.Symbol))
                    {
                        emphasized.Add(problem.Symbol);
                    }
                }
            }

            _currentTask = TrainingGenerator.GenerateTask(content, alphabet, pool, Math.Clamp(settings.GroupCount, 1, 100), emphasized);
            _currentTaskRecorded = false;
            _currentGroupCount = Math.Clamp(settings.GroupCount, 1, 100);
            _currentClip = await Task.Run(() => MorseAudioService.Render(
                _currentTask,
                Math.Clamp(settings.CharactersPerMinute, 20, 300),
                Math.Clamp(settings.FrequencyHz, 300, 1200),
                Math.Clamp(settings.VolumePercent, 0, 100),
                Math.Clamp(settings.CharacterGapUnits, 3, 20),
                Math.Clamp(settings.GroupGapUnits, 7, 30),
                settings.PlayStartSignal,
                Math.Clamp(settings.StartPauseUnits, 7, 60),
                new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz)));
            _answerVisible = false;
            AnswerEditor.Text = string.Empty;
            AccuracyLabel.Text = Texts.T("Точность: —");
            ResultLabel.Text = Texts.T("Пробелы между группами не учитываются");
            UpdateTaskLabel();
            PlayButton.IsEnabled = true;
            RepeatButton.IsEnabled = true;
            CheckButton.IsEnabled = true;
            PlaybackStatusLabel.Text = Texts.F("Готово · {0:mm\\:ss}", _currentClip.Duration);
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Ошибка"), exception.Message, Texts.T("Закрыть"));
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private async void PlayButton_OnClicked(object sender, EventArgs e)
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

        // Экзамен: прослушивание одно
        _exam?.RegisterPlayback();
        StopPlayback();
        _playbackCancellation = new CancellationTokenSource();
        PlayButton.IsEnabled = false;
        RepeatButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        PlaybackStatusLabel.Text = Texts.T("Сначала Ж Ж Ж, затем начнётся задание…");
        try
        {
            var path = await AudioFileService.SaveClipAsync(_currentClip, "current-training.wav", _playbackCancellation.Token);
            await _audioPlayback.PlayAsync(path, _playbackCancellation.Token);
            PlaybackStatusLabel.Text = Texts.T("Готово — введите ответ");
            AnswerEditor.Focus();
        }
        catch (OperationCanceledException)
        {
            PlaybackStatusLabel.Text = Texts.T("Воспроизведение остановлено");
        }
        finally
        {
            _playbackCancellation?.Dispose();
            _playbackCancellation = null;
            PlayButton.IsEnabled = CanPlayNow;
            RepeatButton.IsEnabled = CanPlayNow;
            StopButton.IsEnabled = false;
        }
    }

    private void StopButton_OnClicked(object sender, EventArgs e) => StopPlayback();

    private void StopPlayback()
    {
        _playbackCancellation?.Cancel();
        _audioPlayback.Stop();
    }

    private void ToggleAnswerButton_OnClicked(object sender, EventArgs e)
    {
        _answerVisible = !_answerVisible;
        UpdateTaskLabel();
    }

    private void UpdateTaskLabel()
    {
        TaskLabel.Text = _answerVisible
            ? _currentTask
            : new string(_currentTask.Select(symbol => char.IsWhiteSpace(symbol) ? ' ' : '•').ToArray());
        ToggleAnswerButton.Text = _answerVisible ? Texts.T("Скрыть") : Texts.T("Показать");
    }

    private void CheckButton_OnClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            return;
        }

        var result = TrainingEvaluator.Evaluate(_currentTask, AnswerEditor.Text ?? string.Empty);
        // Экзамен завершается первой проверкой: время останавливается, протокол готов
        ExamResult? examResult = null;
        if (_exam is not null && !_exam.IsFinished)
        {
            examResult = _exam.Finish(AnswerEditor.Text ?? string.Empty, DateTime.Now);
            _examReport = ExamReport.Format(examResult);
            ExamLabel.Text = Texts.F("Экзамен завершён · {0}", ExamReport.FormatDuration(examResult.Duration));
            ExamShareButton.IsVisible = true;
            ToggleAnswerButton.IsEnabled = true;
        }

        // В историю попадает только первая проверка каждого задания
        if (!_currentTaskRecorded)
        {
            _currentTaskRecorded = true;
            var settings = _settingsService.LoadSettings();
            _historyStore.Add(TrainingStatistics.CreateRecord(DateTime.Now, settings.ActiveProfileName,
                settings.CharactersPerMinute, _currentGroupCount, result, examResult is not null));
            UpdateHistoryLabel();
        }
        AccuracyLabel.Text = Texts.F("Точность: {0:0.#}%", result.AccuracyPercent);
        if (result.IsPerfect)
        {
            ResultLabel.Text = Texts.T("Отлично: все символы распознаны правильно");
            ResultLabel.TextColor = (Color)Application.Current!.Resources["PrimaryDark"];
        }
        else
        {
            ResultLabel.Text = Texts.T("Ошибки: ") + string.Join(", ", result.Mistakes.Take(8)
                .Select(FormatMistake));
            ResultLabel.TextColor = (Color)Application.Current!.Resources["Danger"];
        }

        var checkedSettings = _settingsService.LoadSettings();
        if (examResult is not null)
        {
            ResultLabel.Text = ExamReport.Summary(examResult);
        }
        else if (checkedSettings.ContentModeIndex == (int)ContentMode.Koch)
        {
            var kochAlphabet = (AlphabetMode)Math.Clamp(checkedSettings.AlphabetIndex, 0, 2);
            ResultLabel.Text += "\n" + KochMethod.Advice(kochAlphabet, checkedSettings.KochLevel, result.AccuracyPercent);
        }

        _answerVisible = true;
        UpdateTaskLabel();
    }

    // ---------- Экзамен ----------

    private async void ExamButton_OnClicked(object sender, EventArgs e)
    {
        await GenerateTaskAsync();
        if (string.IsNullOrEmpty(_currentTask) || _currentClip is null)
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        _exam = new ExamSession(_currentTask, Math.Clamp(settings.CharactersPerMinute, 20, 300), _currentGroupCount, settings.ActiveProfileName, DateTime.Now);
        _examReport = null;
        // Ответ скрыт до проверки, повтор запрещён: прослушивание одно
        _answerVisible = false;
        UpdateTaskLabel();
        ToggleAnswerButton.IsEnabled = false;
        RepeatButton.IsEnabled = false;
        ExamShareButton.IsVisible = false;
        ExamLabel.IsVisible = true;
        UpdateExamLabel();
        if (!_examTimerRunning)
        {
            _examTimerRunning = true;
            Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                UpdateExamLabel();
                _examTimerRunning = _exam is not null && !_exam.IsFinished;
                return _examTimerRunning;
            });
        }

        await PlayCurrentAsync();
    }

    private void UpdateExamLabel()
    {
        if (_exam is null || _exam.IsFinished)
        {
            return;
        }

        ExamLabel.Text = Texts.F("Экзамен · одно прослушивание · {0}", ExamReport.FormatDuration(_exam.Elapsed(DateTime.Now)));
    }

    /// <summary>Новое задание отменяет экзамен.</summary>
    private void EndExam()
    {
        _exam = null;
        _examReport = null;
        ExamLabel.IsVisible = false;
        ExamShareButton.IsVisible = false;
        ToggleAnswerButton.IsEnabled = true;
    }

    private async void ExamShareButton_OnClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_examReport))
        {
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(_examReport);
            await Share.Default.RequestAsync(new ShareTextRequest { Title = Texts.T("Протокол экзамена Morse Trainer"), Text = _examReport });
            PlaybackStatusLabel.Text = Texts.T("Протокол скопирован в буфер и отправлен");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private void UpdateHistoryLabel()
    {
        var summary = TrainingStatistics.Summarize(_historyStore.Load());
        HistoryLabel.Text = summary.Sessions == 0
            ? string.Empty
            : Texts.F("За всё время: {0} · средняя точность {1:0.#}% · лучшая {2:0.#}%", summary.Sessions, summary.AverageAccuracy, summary.BestAccuracy);
    }

    private static string FormatMistake(CharacterMistake mistake)
    {
        var actual = mistake.Actual?.ToString() ?? "∅";
        return $"{mistake.Position}: {mistake.Expected}→{actual}";
    }

    // Планшет или альбомная ориентация: центрируем контент полосой до 720 px
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            RootLayout.Padding = TabletLayout.PaddingFor(width);
        }
    }
}
