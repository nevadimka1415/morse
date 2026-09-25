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

/// <summary>Главное окно: вкладка «Обучение»: курс «С нуля до 60 зн/мин», карточки символов, свои напевы и голос.</summary>
public partial class MainWindow
{
    private static readonly string[] OwnVoiceExtensions = { ".wav" };

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


    private Button[] LearningAlphabetButtons => new[] { LearningRussianButton, LearningLatinButton, LearningBothButton, LearningDigitsButton };

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
        // Символы со своей записью голоса (папка голоса) — с пометкой «ваш голос» в подписи карточки
        var items = LearningCatalog.GetItems(_learningAlphabet)
            .Select(item => CustomVoice.Find(AppPaths.VoiceDirectory, item.Symbol, OwnVoiceExtensions) is null ? item : item with { HasOwnVoice = true })
            .ToArray();
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
}
