using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
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
    private bool _currentTaskRecorded;
    private DateTime _currentTaskStartedAt = DateTime.Now;
    private DrillPlan? _currentDrill;
    private int _currentGroupCount;
    private CancellationTokenSource? _playbackCancellation;
    private AudioClip? _currentClip;
    // Настройки, по которым отрисовано текущее задание: повтор одной группы звучит так же (скорость, тон, паузы, помехи)
    private AppSettings? _renderSettings;
    private readonly PracticeTracker _practice;
    private string _currentTask = string.Empty;
    private bool _answerVisible;
    private int _playingGroup;   // какая группа звучит (с 1); 0 — не звучит или идёт сигнал Ж Ж Ж
    private const string AnswerPanelKey = "training.typed_answer";
    private const string ParamsPanelKey = "training.params_open";

    public TrainingPage(IAudioPlaybackService audioPlayback, MobileSettingsService settingsService, TrainingHistoryStore historyStore,
        PracticeTracker practice)
    {
        InitializeComponent();
        _practice = practice;
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _historyStore = historyStore;
        ParamsPanel.Attach(settingsService);
        SetAnswerPanel(Preferences.Default.Get(AnswerPanelKey, false), remember: false);
        SetParamsPanel(Preferences.Default.Get(ParamsPanelKey, false));
    }

    // Окно закрыто (Android пересоздал активность): страница больше не показывается — остановка звука,
    // чтобы старая страница не трогала свои элементы
    public void Dispose()
    {
        _playbackCancellation?.Cancel();
        _audioPlayback.Stop();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Параметры могли поменять шаг курса («Обучение») или перенос настроек («Настройки»)
        ParamsPanel.Reload();
        UpdateHistoryLabel();
        if (string.IsNullOrEmpty(_currentTask))
        {
            await GenerateTaskAsync();
        }
    }

    private void ParamsToggleButton_OnClicked(object sender, EventArgs e)
    {
        SetParamsPanel(!ParamsPanel.IsVisible);
        Preferences.Default.Set(ParamsPanelKey, ParamsPanel.IsVisible);
    }

    /// <summary>Параметры тренировки: по умолчанию свёрнуты, раскрываются кнопкой «Параметры ▾» в шапке.</summary>
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
        var settings = _settingsService.LoadSettings();
        var alphabet = (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2);
        var content = ContentModes.Clamp(settings.ContentModeIndex);
        var pool = drill?.Pool ?? MorseAlphabet.BuildPool(alphabet, content, settings.CustomSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            await DisplayAlertAsync(Texts.T("Нет символов"), Texts.T("Откройте «Параметры» и выберите хотя бы один символ."), Texts.T("Понятно"));
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

            var task = drill is null
                ? TrainingGenerator.GenerateTask(content, alphabet, pool, Math.Clamp(settings.GroupCount, 1, 100), emphasized)
                : TrainingGenerator.Generate(pool, Math.Clamp(settings.GroupCount, 1, 100), emphasized);
            // Задание становится текущим только после отрисовки звука: пока доигрывает finally старого прослушивания,
            // на экране остаётся старое задание — новое не мелькнёт открытым текстом
            var clip = await Task.Run(() => Render(task, settings));
            _currentTask = task;
            _currentClip = clip;
            _renderSettings = settings;
            _currentDrill = drill;
            _currentTaskRecorded = false;
            _currentTaskStartedAt = DateTime.Now;
            _currentGroupCount = Math.Clamp(settings.GroupCount, 1, 100);
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
            UpdateShareAudioButton();
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

    private bool CanPlayNow => _currentClip is not null;

    /// <summary>Звук задания по параметрам: скорость, тон, громкость, паузы, сигнал старта и помехи.</summary>
    private static AudioClip Render(string task, AppSettings settings) => MorseAudioService.Render(
        task,
        Math.Clamp(settings.CharactersPerMinute, 20, 300),
        Math.Clamp(settings.FrequencyHz, 300, 1200),
        Math.Clamp(settings.VolumePercent, 0, 100),
        Math.Clamp(settings.CharacterGapUnits, 3, 20),
        Math.Clamp(settings.GroupGapUnits, 7, 30),
        settings.PlayStartSignal,
        Math.Clamp(settings.StartPauseUnits, 7, 60),
        new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz));

    /// <summary>
    /// Параметры поменяли после создания задания: изменился состав (режим, символы, алфавит, уровень Коха, число
    /// групп) — создаётся новое задание; изменился только звук (скорость, тон, громкость, паузы, сигнал старта,
    /// помехи) — то же задание звучит по-новому. Так «Слушать» всегда играет то, что выставлено в «Параметрах».
    /// </summary>
    private async Task RefreshForChangedSettingsAsync()
    {
        if (_renderSettings is not { } old)
        {
            return;
        }

        var now = _settingsService.LoadSettings();
        var contentChanged = old.AlphabetIndex != now.AlphabetIndex || old.ContentModeIndex != now.ContentModeIndex
                             || old.CustomSymbols != now.CustomSymbols || old.KochLevel != now.KochLevel || old.GroupCount != now.GroupCount;
        // Упражнение на ошибки собирает свой состав — смена режима его не отменяет
        if (contentChanged && _currentDrill is null)
        {
            await GenerateTaskAsync();
            return;
        }

        var soundChanged = old.CharactersPerMinute != now.CharactersPerMinute || old.FrequencyHz != now.FrequencyHz
                           || old.VolumePercent != now.VolumePercent || old.CharacterGapUnits != now.CharacterGapUnits
                           || old.GroupGapUnits != now.GroupGapUnits || old.PlayStartSignal != now.PlayStartSignal
                           || old.StartPauseUnits != now.StartPauseUnits || old.NoisePercent != now.NoisePercent
                           || old.QsbPercent != now.QsbPercent || old.DriftHz != now.DriftHz;
        if (soundChanged)
        {
            var task = _currentTask;
            _currentClip = await Task.Run(() => Render(task, now));
            _renderSettings = now;
        }
    }

    private async Task PlayCurrentAsync()
    {
        if (_currentClip is null)
        {
            return;
        }

        await RefreshForChangedSettingsAsync();
        if (_currentClip is null)
        {
            return;
        }

        StopPlayback();
        var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        PlayButton.IsEnabled = false;
        RepeatButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        var clip = _currentClip;
        // Пока звучит задание, экран не гаснет: при записи на бумаге смотрят на «Группа N из M»
        DeviceDisplay.Current.KeepScreenOn = true;
        // «Сначала Ж Ж Ж» — только когда сигнал начала действительно звучит
        PlaybackStatusLabel.Text = clip.GroupAt(TimeSpan.Zero) == 0
            ? Texts.T("Сначала Ж Ж Ж, затем начнётся задание…")
            : Texts.T("Идёт воспроизведение…");
        var playing = true;
        try
        {
            var path = await AudioFileService.SaveClipAsync(clip, "current-training.wav", cancellation.Token);
            // Счётчик групп по времени от начала воспроизведения: «Группа 3 из 10» и подсветка группы в тексте задания
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // Без сигнала начала первая группа звучит сразу — «Ж Ж Ж» не показываем даже на миг
            ShowPlayingGroup(clip, clip.GroupAt(TimeSpan.Zero));
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(200), () =>
            {
                if (!playing)
                {
                    return false;
                }

                ShowPlayingGroup(clip, clip.GroupAt(clock.Elapsed));
                return true;
            });
            await _audioPlayback.PlayAsync(path, cancellation.Token);
            // Задание дослушано — занятие (на бумаге в историю ничего не пишется): для серии дней и напоминания
            var practiced = _settingsService.LoadSettings();
            _practice.Mark(practiced);
            _settingsService.SaveSettings(practiced);
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
            // Новое прослушивание уже идёт — его статус не перетираем
            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                PlaybackStatusLabel.Text = Texts.T("Воспроизведение остановлено");
            }
        }
        catch (Exception exception)
        {
            // Сбой звука (MediaPlayer, запись файла) не должен ронять приложение из async void;
            // счётчик групп останавливается сразу, а не после закрытия окна ошибки
            playing = false;
            HidePlayingGroup();
            PlaybackStatusLabel.Text = Texts.T("Не удалось воспроизвести");
            await DisplayAlertAsync(Texts.T("Не удалось воспроизвести"), exception.Message, Texts.T("Закрыть"));
        }
        finally
        {
            playing = false;
            // Новое прослушивание уже началось — его счётчик, кнопки и отмену не трогаем
            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                DeviceDisplay.Current.KeepScreenOn = false;
                HidePlayingGroup();
                _playbackCancellation = null;
                PlayButton.IsEnabled = CanPlayNow;
                RepeatButton.IsEnabled = CanPlayNow;
                StopButton.IsEnabled = false;
            }

            cancellation.Dispose();
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
    /// Шаги курса засчитываются по введённому ответу — там поле раскрывается само, без запоминания.
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
        ToggleAnswerButton.Text = _answerVisible ? Texts.T("Скрыть") : Texts.T("Показать");
        // Группы — отдельными кусками текста: звучащая выделена цветом, нажатие на группу повторяет только её
        var text = new FormattedString();
        var groups = MorseAudioService.SplitGroups(_currentTask);
        for (var index = 0; index < groups.Length; index++)
        {
            if (index > 0)
            {
                text.Spans.Add(new Span { Text = " " });
            }

            var span = new Span { Text = _answerVisible ? groups[index] : new string('•', groups[index].Length) };
            if (index + 1 == _playingGroup)
            {
                span.TextColor = (Color)Application.Current!.Resources["PrimaryDark"];
                span.FontAttributes = FontAttributes.Bold;
            }

            if (CanReplayGroup)
            {
                var group = index;
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) => await PlayGroupAsync(group);
                span.GestureRecognizers.Add(tap);
            }

            text.Spans.Add(span);
        }

        TaskLabel.FormattedText = text;
    }

    /// <summary>Повтор одной группы — как только задание готово.</summary>
    private bool CanReplayGroup => _currentClip is not null && _renderSettings is not null;

    /// <summary>Нажатие на группу в тексте задания — звучит только она, с теми же скоростью, тоном, паузами и помехами.</summary>
    private async Task PlayGroupAsync(int index)
    {
        var groups = MorseAudioService.SplitGroups(_currentTask);
        if (!CanReplayGroup || _renderSettings is not { } settings || index < 0 || index >= groups.Length)
        {
            return;
        }

        StopPlayback();
        try
        {
            var group = groups[index];
            var clip = await Task.Run(() => MorseAudioService.Render(
                group,
                Math.Clamp(settings.CharactersPerMinute, 20, 300),
                Math.Clamp(settings.FrequencyHz, 300, 1200),
                Math.Clamp(settings.VolumePercent, 0, 100),
                Math.Clamp(settings.CharacterGapUnits, 3, 20),
                Math.Clamp(settings.GroupGapUnits, 7, 30),
                false,
                noise: new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz)));
            var path = await AudioFileService.SaveClipAsync(clip, "current-group.wav");
            PlaybackStatusLabel.Text = Texts.F("Повтор группы {0}", index + 1);
            await _audioPlayback.PlayAsync(path);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // Сбой звука не должен ронять приложение из обработчика нажатия
            PlaybackStatusLabel.Text = Texts.T("Не удалось воспроизвести");
        }
    }

    /// <summary>«Поделиться звуком задания» — для готового задания.</summary>
    private void UpdateShareAudioButton() => ShareAudioButton.IsEnabled = _currentClip is not null;

    // Задание аудиофайлом: слушать в любом плеере, в машине, в наушниках или отправить другу; текст для сверки — рядом файлом
    private async void ShareAudioButton_OnClicked(object sender, EventArgs e)
    {
        if (_currentClip is not { } clip)
        {
            return;
        }

        try
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            var audio = Path.Combine(FileSystem.CacheDirectory, $"morse-task-{stamp}.wav");
            await File.WriteAllBytesAsync(audio, clip.WavBytes);
            var text = Path.Combine(FileSystem.CacheDirectory, $"morse-task-{stamp}.txt");
            await File.WriteAllTextAsync(text,
                Texts.T("Текст задания — откройте после прослушивания:") + Environment.NewLine + Environment.NewLine + _currentTask + Environment.NewLine);
            await Share.Default.RequestAsync(new ShareMultipleFilesRequest
            {
                Title = Texts.T("Задание Morse Trainer"),
                Files = new List<ShareFile> { new(audio, "audio/wav"), new(text, "text/plain") }
            });
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    /// <summary>«Группа 3 из 10» над кнопками и подсветка группы в тексте задания; 0 — ещё звучит сигнал Ж Ж Ж.</summary>
    private void ShowPlayingGroup(AudioClip clip, int group)
    {
        if (clip.GroupCount == 0)
        {
            return;
        }

        GroupCounterLabel.IsVisible = true;
        GroupCounterLabel.Text = group == 0 ? Texts.T("Сигнал начала: Ж Ж Ж") : Texts.F("Группа {0} из {1}", group, clip.GroupCount);
        if (group != _playingGroup)
        {
            _playingGroup = group;
            UpdateTaskLabel();
        }
    }

    private void HidePlayingGroup()
    {
        GroupCounterLabel.IsVisible = false;
        if (_playingGroup != 0)
        {
            _playingGroup = 0;
            UpdateTaskLabel();
        }
    }

    private void CheckButton_OnClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            return;
        }

        var result = TrainingEvaluator.Evaluate(_currentTask, AnswerEditor.Text ?? string.Empty);
        var autoSpeedNote = string.Empty;

        // В историю попадает только первая проверка каждого задания
        if (!_currentTaskRecorded)
        {
            _currentTaskRecorded = true;
            var settings = _settingsService.LoadSettings();
            // Задание по настройкам текущего шага курса помечается его номером — так считается зачёт шага
            var courseStep = _currentDrill is null ? Course.StepForRecord(settings) : 0;
            var history = _historyStore.Add(TrainingStatistics.CreateRecord(DateTime.Now, settings.ActiveProfileName,
                settings.CharactersPerMinute, _currentGroupCount, result, DateTime.Now - _currentTaskStartedAt, courseStep));
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
                    // Новая скорость видна в «Параметрах» сразу, а не после ухода с вкладки
                    ParamsPanel.Reload();
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
        if (_currentDrill is null && checkedSettings.ContentModeIndex == (int)ContentMode.Koch)
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

    /// <summary>Новое задание по текущим параметрам с полем для ответа (кнопки курса на странице «Обучение»).</summary>
    public async Task StartTaskAsync()
    {
        SetAnswerPanel(true, remember: false);
        await GenerateTaskAsync();
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
