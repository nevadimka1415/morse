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
using Microsoft.Win32;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer;

/// <summary>Главное окно: вкладка «Тренировка»: задание, прослушивание, запись на бумаге или ввод ответа.</summary>
public partial class MainWindow
{
    private async void GenerateButton_OnClick(object sender, RoutedEventArgs e)
    {
        await GenerateTaskAsync();
    }

    /// <summary>Новое задание по параметрам.</summary>
    private async Task GenerateTaskAsync()
    {
        if (!TryReadGroupCount(out var groupCount))
        {
            return;
        }

        var settings = ReadSettings();
        settings.GroupCount = groupCount;
        var alphabet = (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2);
        var content = ContentModes.Clamp(ContentModeCombo.SelectedIndex);
        var pool = MorseAlphabet.BuildPool(alphabet, content, _selectedSymbols, settings.KochLevel);
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

            var generatedTask = TrainingGenerator.GenerateTask(content, alphabet, pool, settings.GroupCount, emphasized);
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
            _answerVisible = false;
            UserAnswerText.Clear();
            ResultDetailsText.Text = Texts.T("Пробелы между группами при проверке не учитываются");
            ResultDetailsText.Foreground = (Brush)FindResource("MutedTextBrush");
            AccuracyText.Text = "—";
            PlaybackProgress.Value = 0;
            PlaybackStatusText.Text = Texts.F("Готово к воспроизведению · {0}", FormatDuration(audio.Duration));
            var startSignal = settings.PlayStartSignal ? Texts.T(" · старт Ж Ж Ж") : string.Empty;
            if (!new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz).IsClean)
            {
                startSignal += Texts.T(" · помехи");
            }

            var metaTemplate = ContentModes.IsWordMode(content)
                ? Texts.T("{0} слов · {1} знаков/мин · паузы {2}/{3}{4}")
                : Texts.T("{0} групп × 5 · {1} знаков/мин · паузы {2}/{3}{4}");
            TaskMetaText.Text = string.Format(CultureInfo.CurrentCulture, metaTemplate, settings.GroupCount, settings.CharactersPerMinute, settings.CharacterGapUnits, settings.GroupGapUnits, startSignal);
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

    private bool CanPlayNow => _currentClip is not null;

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
            PlaybackStatusText.Text = Texts.T("Идёт воспроизведение…");
            SetStatus(Texts.T("СЛУШАЕМ"), isActive: true);

            var startedAt = DateTime.UtcNow;
            // Без сигнала начала первая группа звучит сразу — «Ж Ж Ж» не показываем даже на миг
            ShowPlayingGroup(_currentClip.GroupAt(TimeSpan.Zero));
            while (DateTime.UtcNow - startedAt < _currentClip.Duration)
            {
                await Task.Delay(50, cancellation.Token);
                var elapsed = DateTime.UtcNow - startedAt;
                PlaybackProgress.Value = Math.Min(100, elapsed.TotalMilliseconds / _currentClip.Duration.TotalMilliseconds * 100);
                ShowPlayingGroup(_currentClip.GroupAt(elapsed));
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
            // Новое прослушивание уже началось — его счётчик групп не трогаем
            if (_playbackCancellation is null || ReferenceEquals(_playbackCancellation, cancellation))
            {
                HidePlayingGroup();
            }

            if (ReferenceEquals(_playbackCancellation, cancellation))
            {
                PlayButton.IsEnabled = CanPlayNow;
                RepeatButton.IsEnabled = CanPlayNow;
                StopButton.IsEnabled = false;
                _playbackCancellation = null;
            }
        }
    }

    /// <summary>
    /// «Группа 3 из 10» над текстом задания и подсветка этой группы среди точек (или букв, если текст открыт) —
    /// чтобы при записи на бумаге не сбиться. 0 — ещё звучит сигнал Ж Ж Ж.
    /// </summary>
    internal void ShowPlayingGroup(int group)
    {
        if (_currentClip is null || _currentClip.GroupCount == 0)
        {
            return;
        }

        GroupCounterText.Visibility = Visibility.Visible;
        GroupCounterText.Text = group == 0
            ? Texts.T("Сигнал начала: Ж Ж Ж")
            : Texts.F("Группа {0} из {1}", group, _currentClip.GroupCount);
        if (group != _playingGroup)
        {
            _playingGroup = group;
            UpdateAnswerDisplay();
        }
    }

    internal void HidePlayingGroup()
    {
        GroupCounterText.Visibility = Visibility.Collapsed;
        if (_playingGroup != 0)
        {
            _playingGroup = 0;
            UpdateAnswerDisplay();
        }
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e) => StopPlayback();

    /// <summary>Занятие без записи в истории (задание дослушано, ответ «На слух»): плашка и напоминание его учитывают.</summary>
    private void MarkPracticed()
    {
        _lastPracticeAt = DateTime.Now;
        // День занятия — для серии «дней подряд» в напоминании (занятия на бумаге в историю не пишутся)
        PracticeNudge.MarkPracticeDay(_practiceDays, _lastPracticeAt.Value);
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
    /// С вводом видны точность и попытки и появляется вкладка «Прогресс»; шаги курса засчитываются
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
        if (GroupCounterText is not null)
        {
            HidePlayingGroup();
        }

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

        ToggleAnswerButton.Content = _answerVisible ? Texts.T("Скрыть") : Texts.T("Показать");
        // Группы — отдельными кусками текста: звучащая выделена цветом, щелчок по группе повторяет только её
        AnswerDisplayText.Inlines.Clear();
        var groups = MorseAudioService.SplitGroups(_currentTask);
        var replay = CanReplayGroup;
        for (var index = 0; index < groups.Length; index++)
        {
            if (index > 0)
            {
                AnswerDisplayText.Inlines.Add(new Run(" "));
            }

            var run = new Run(_answerVisible ? groups[index] : new string('\u2022', groups[index].Length));
            if (index + 1 == _playingGroup)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "PrimaryBrush");
                run.FontWeight = FontWeights.ExtraBold;
            }

            if (replay)
            {
                var group = index;
                run.Cursor = Cursors.Hand;
                run.ToolTip = Texts.T("Щелчок — прозвучит только эта группа");
                run.MouseLeftButtonUp += (_, _) => PlayGroup(group);
            }

            AnswerDisplayText.Inlines.Add(run);
        }
    }

    /// <summary>Повтор одной группы доступен, когда есть готовое задание и его звук.</summary>
    private bool CanReplayGroup => _currentClip is not null && _currentSettings is not null;

    /// <summary>Щелчок по группе в тексте задания — звучит только она, с теми же скоростью, тоном, паузами и помехами.</summary>
    internal void PlayGroup(int index)
    {
        var groups = MorseAudioService.SplitGroups(_currentTask);
        if (!CanReplayGroup || _currentSettings is not { } settings || index < 0 || index >= groups.Length)
        {
            return;
        }

        StopPlayback();
        StopLearningPlayback();
        try
        {
            var clip = MorseAudioService.Render(groups[index], settings.CharactersPerMinute, settings.FrequencyHz, settings.VolumePercent,
                settings.CharacterGapUnits, settings.GroupGapUnits, false, noise: new NoiseProfile(settings.NoisePercent, settings.QsbPercent, settings.DriftHz));
            _learningAudioStream = new MemoryStream(clip.WavBytes, writable: false);
            _learningPlayer = new SoundPlayer(_learningAudioStream);
            _learningPlayer.Load();
            _learningPlayer.Play();
            PlaybackStatusText.Text = Texts.F("Повтор группы {0}", index + 1);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            PlaybackStatusText.Text = Texts.T("Не удалось воспроизвести");
        }
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
        // В историю попадает только первая проверка каждого задания
        if (!_currentTaskRecorded)
        {
            _currentTaskRecorded = true;
            var taskSettings = _currentSettings ?? ReadSettings();
            // Задание по настройкам текущего шага курса помечается его номером — так считается зачёт шага
            var courseStep = Course.StepForRecord(taskSettings);
            var record = TrainingStatistics.CreateRecord(DateTime.Now, taskSettings.ActiveProfileName,
                taskSettings.CharactersPerMinute, _currentTask.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, result,
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

        if (_currentSettings?.ContentModeIndex == (int)ContentMode.Koch)
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

    private void SaveTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
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
            .AppendLine(ContentModes.IsWordMode(ContentModes.Clamp(settings.ContentModeIndex))
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
