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

/// <summary>Главное окно: вкладка «На слух»: вопрос, похожие варианты, своя скорость, следующий символ сам, клавиатура.</summary>
public partial class MainWindow
{
    private Button[] QuizAlphabetButtons => new[] { QuizRussianButton, QuizLatinButton, QuizBothButton, QuizDigitsButton };

    private Button[] QuizAnswerButtons => new[] { QuizAnswer0Button, QuizAnswer1Button, QuizAnswer2Button, QuizAnswer3Button };

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
        // Символы, которые путали, звучат чаще
        var question = EarQuiz.Next(LearningCatalog.GetItems(_quizAlphabet), _quizTarget?.Symbol, null, _quizMisses);
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

        EarQuiz.Record(_quizMisses, _quizTarget.Symbol, correct);
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
        _quizMisses.Clear();   // сброс счёта — и все символы снова звучат одинаково часто
        UpdateQuizScore();
        SaveSettingsIfLoaded();
    }

    private void UpdateQuizScore()
    {
        QuizScoreText.Text = Texts.F("Результат: {0} / {1}", _quizCorrect, _quizTotal);
        var frequent = EarQuiz.Frequent(_quizMisses);
        QuizFrequentText.Text = frequent.Count == 0 ? string.Empty : Texts.F("Чаще звучат: {0}", string.Join(' ', frequent));
        QuizFrequentText.Visibility = frequent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void ClearQuizMark(Button button)
    {
        button.ClearValue(BackgroundProperty);
        button.ClearValue(ForegroundProperty);
    }
}
