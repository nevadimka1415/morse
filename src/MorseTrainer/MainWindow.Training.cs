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

/// <summary>Главное окно: вкладка «Тренировка»: задание, прослушивание, запись на бумаге или ввод ответа, экзамен, повтор сложных.</summary>
public partial class MainWindow
{
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
        AnswerPromptText.Visibility = visibility;
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
}
