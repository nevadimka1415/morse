using Microsoft.Maui.ApplicationModel.DataTransfer;
using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile.Pages;

public partial class TrainingPage : ContentPage, IDisposable
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private readonly TrainingHistoryStore _historyStore;
    private readonly EventHandler _settingsChanged;
    private bool _currentTaskRecorded;
    private DateTime _currentTaskStartedAt = DateTime.Now;
    private ExamSession? _exam;
    private DrillPlan? _currentDrill;
    private string? _examReport;
    private bool _examTimerRunning;
    private int _currentGroupCount;
    private CancellationTokenSource? _playbackCancellation;
    private AudioClip? _currentClip;
    private string _currentTask = string.Empty;
    private bool _answerVisible;
    private const string AnswerPanelKey = "training.typed_answer";
    private const string ParamsPanelKey = "training.params_open";

    public TrainingPage(IAudioPlaybackService audioPlayback, MobileSettingsService settingsService, TrainingHistoryStore historyStore)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _historyStore = historyStore;
        _settingsChanged = (_, _) => MainThread.BeginInvokeOnMainThread(RefreshSettingsSummary);
        _settingsService.SettingsChanged += _settingsChanged;
        SetAnswerPanel(Preferences.Default.Get(AnswerPanelKey, false), remember: false);
        SetParamsPanel(Preferences.Default.Get(ParamsPanelKey, false));
    }

    // Окно закрыто (Android пересоздал активность): страница больше не показывается — отписка от настроек,
    // остановка звука и таймера экзамена, чтобы старая страница не трогала свои элементы
    public void Dispose()
    {
        _settingsService.SettingsChanged -= _settingsChanged;
        _playbackCancellation?.Cancel();
        _audioPlayback.Stop();
        _exam = null;
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

    private void ParamsToggleButton_OnClicked(object sender, EventArgs e)
    {
        SetParamsPanel(!ParamsPanel.IsVisible);
        Preferences.Default.Set(ParamsPanelKey, ParamsPanel.IsVisible);
    }

    /// <summary>Сводка параметров задания: по умолчанию свёрнута, раскрывается кнопкой «Параметры ▾» в шапке.</summary>
    private void SetParamsPanel(bool visible)
    {
        ParamsPanel.IsVisible = visible;
        ParamsToggleButton.Text = visible ? Texts.T("Параметры ▴") : Texts.T("Параметры ▾");
    }

    private async void GenerateButton_OnClicked(object sender, EventArgs e)
    {
        await GenerateTaskAsync();
    }

    /// <summary>Новое задание по настройкам; drill — упражнение «Повторить сложные символы» вместо обычного состава.</summary>
    private async Task GenerateTaskAsync(DrillPlan? drill = null)
    {
        StopPlayback();
        EndExam();
        var settings = _settingsService.LoadSettings();
        var alphabet = (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2);
        var content = ContentModes.Clamp(settings.ContentModeIndex);
        var pool = drill?.Pool ?? MorseAlphabet.BuildPool(alphabet, content, settings.CustomSymbols, settings.KochLevel);
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
                foreach (var problem in TrainingStatistics.ProblemSymbols(_historyStore.Load(), 6))
                {
                    if (pool.Contains(problem.Symbol))
                    {
                        emphasized.Add(problem.Symbol);
                    }
                }
            }

            _currentTask = drill is null
                ? TrainingGenerator.GenerateTask(content, alphabet, pool, Math.Clamp(settings.GroupCount, 1, 100), emphasized)
                : TrainingGenerator.Generate(pool, Math.Clamp(settings.GroupCount, 1, 100), emphasized);
            _currentDrill = drill;
            _currentTaskRecorded = false;
            _currentTaskStartedAt = DateTime.Now;
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
            ResultLabel.Text = drill?.Describe() ?? Texts.T("Пробелы между группами не учитываются");
            ResultLabel.ClearValue(Label.TextColorProperty);
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

        // Экзамен: прослушиваний не больше, чем разрешено правилами
        _exam?.RegisterPlayback();
        UpdateExamLabel();
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
            // Клавиатура нужна только при вводе ответа; при записи на бумаге она закрыла бы полэкрана
            if (AnswerPanel.IsVisible)
            {
                PlaybackStatusLabel.Text = Texts.T("Готово — введите ответ");
                AnswerEditor.Focus();
            }
            else
            {
                PlaybackStatusLabel.Text = Texts.T("Готово — сверьте запись с текстом задания");
            }
        }
        catch (OperationCanceledException)
        {
            PlaybackStatusLabel.Text = Texts.T("Воспроизведение остановлено");
        }
        catch (Exception exception)
        {
            // Сбой звука (MediaPlayer, запись файла) не должен ронять приложение из async void
            PlaybackStatusLabel.Text = Texts.T("Не удалось воспроизвести");
            await DisplayAlertAsync(Texts.T("Не удалось воспроизвести"), exception.Message, Texts.T("Закрыть"));
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

    private void AnswerPanelButton_OnClicked(object sender, EventArgs e) => SetAnswerPanel(!AnswerPanel.IsVisible, remember: true);

    /// <summary>
    /// Ввод ответа — по желанию: обычно группы пишут на бумаге и сверяют с текстом задания («Показать»).
    /// Экзамен и шаги курса засчитываются по введённому ответу — там поле раскрывается само, без запоминания.
    /// </summary>
    private void SetAnswerPanel(bool visible, bool remember)
    {
        AnswerPanel.IsVisible = visible;
        PaperHintLabel.IsVisible = !visible;
        AnswerPanelButton.Text = visible ? Texts.T("Проверить вводом ▴") : Texts.T("Проверить вводом ▾");
        if (remember)
        {
            Preferences.Default.Set(AnswerPanelKey, visible);
        }
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
        var autoSpeedNote = string.Empty;
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
            // Задание по настройкам текущего шага курса помечается его номером — так считается зачёт шага
            var courseStep = _currentDrill is null ? Course.StepForRecord(settings, examResult is not null) : 0;
            var history = _historyStore.Add(TrainingStatistics.CreateRecord(DateTime.Now, settings.ActiveProfileName,
                settings.CharactersPerMinute, _currentGroupCount, result, examResult is not null, DateTime.Now - _currentTaskStartedAt, courseStep));
            UpdateHistoryLabel();
            // Лестница скорости: новая скорость сохраняется в настройки и попадёт в следующее задание
            if (settings.AutoSpeed)
            {
                var nextSpeed = SpeedLadder.Next(history, settings.CharactersPerMinute, settings.ActiveProfileName);
                if (nextSpeed != settings.CharactersPerMinute)
                {
                    autoSpeedNote = SpeedLadder.Describe(settings.CharactersPerMinute, nextSpeed);
                    settings.CharactersPerMinute = nextSpeed;
                    _settingsService.SaveSettings(settings);
                }
            }
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
            ResultLabel.Text = ExamReport.Summary(examResult) + "\n" +
                               TrainingStatistics.Exams(TrainingStatistics.ForProfile(_historyStore.Load(), examResult.ProfileName)).Describe();
        }
        else if (_currentDrill is null && checkedSettings.ContentModeIndex == (int)ContentMode.Koch)
        {
            var kochAlphabet = (AlphabetMode)Math.Clamp(checkedSettings.AlphabetIndex, 0, 2);
            ResultLabel.Text += "\n" + KochMethod.Advice(kochAlphabet, checkedSettings.KochLevel, result.AccuracyPercent);
        }

        if (autoSpeedNote.Length > 0)
        {
            ResultLabel.Text += "\n" + autoSpeedNote;
        }

        _answerVisible = true;
        UpdateTaskLabel();
    }

    // ---------- Повтор сложных символов ----------

    private async void DrillButton_OnClicked(object sender, EventArgs e)
    {
        var plan = ProblemDrill.Build(_historyStore.Load());
        if (plan.IsEmpty)
        {
            await DisplayAlertAsync(Texts.T("Повторить сложные символы"),
                Texts.T("Ошибок в истории пока нет. Пройдите несколько заданий: символы, в которых вы ошибётесь, и похожие на них по коду попадут в это упражнение."),
                Texts.T("Понятно"));
            return;
        }

        await GenerateTaskAsync(plan);
    }

    // ---------- Экзамен ----------

    private async void ExamButton_OnClicked(object sender, EventArgs e) => await StartExamAsync();

    /// <summary>Новое задание или экзамен по текущим настройкам (кнопки курса на странице «Обучение»).</summary>
    public async Task StartTaskAsync(bool exam)
    {
        SetAnswerPanel(true, remember: false);
        if (exam)
        {
            await StartExamAsync();
        }
        else
        {
            await GenerateTaskAsync();
        }
    }

    private async Task StartExamAsync()
    {
        SetAnswerPanel(true, remember: false);
        await GenerateTaskAsync();
        if (string.IsNullOrEmpty(_currentTask) || _currentClip is null)
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        _exam = new ExamSession(_currentTask, Math.Clamp(settings.CharactersPerMinute, 20, 300), _currentGroupCount, settings.ActiveProfileName, DateTime.Now,
            settings.ExamPlaybacks, settings.ExamTimeLimitMinutes);
        _examReport = null;
        // Ответ скрыт до проверки; повтор доступен, пока не кончились прослушивания
        _answerVisible = false;
        UpdateTaskLabel();
        ToggleAnswerButton.IsEnabled = false;
        RepeatButton.IsEnabled = false;
        ExamShareButton.IsVisible = false;
        ExamLabel.IsVisible = true;
        UpdateExamLabel();
        ResultLabel.Text = Texts.F("Правила экзамена: {0}", ExamReport.Rules(_exam.MaxPlaybacks, _exam.TimeLimit));
        if (!_examTimerRunning)
        {
            _examTimerRunning = true;
            Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                UpdateExamLabel();
                // Лимит времени вышел — ответ проверяется сам, как по кнопке
                if (_exam is { IsFinished: false } && _exam.IsTimeUp(DateTime.Now))
                {
                    StopPlayback();
                    CheckButton_OnClicked(CheckButton, EventArgs.Empty);
                }

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

        ExamLabel.Text = _exam.Status(DateTime.Now);
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
