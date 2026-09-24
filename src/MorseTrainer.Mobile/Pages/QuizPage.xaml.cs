using System.Security.Cryptography;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Pages;

/// <summary>Проверка на слух: звучит символ, из четырёх вариантов нужно выбрать прозвучавший.</summary>
public partial class QuizPage : ContentPage
{
    private const string AlphabetKey = "quiz.alphabet";

    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private readonly Button[] _alphabetButtons;
    private readonly Button[] _answerButtons;
    private LearningSymbolItem? _target;
    private bool _answered = true;
    private int _correct;
    private int _total;
    private int _alphabet;

    public QuizPage(IAudioPlaybackService audioPlayback, MobileSettingsService settingsService)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _alphabetButtons = new[] { AlphabetRussianButton, AlphabetLatinButton, AlphabetBothButton, AlphabetDigitsButton };
        _answerButtons = new[] { Answer0Button, Answer1Button, Answer2Button, Answer3Button };
        _alphabet = ChoiceButtons.LoadAlphabet(AlphabetKey);
        ChoiceButtons.Highlight(_alphabetButtons, _alphabet);
        var settings = _settingsService.LoadSettings();
        _correct = settings.QuizCorrect;
        _total = settings.QuizTotal;
        UpdateScore();
    }

    private void AlphabetButton_OnClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string parameter } || !int.TryParse(parameter, out var index))
        {
            return;
        }

        _alphabet = Math.Clamp(index, 0, 3);
        ChoiceButtons.SaveAlphabet(AlphabetKey, _alphabet);
        ChoiceButtons.Highlight(_alphabetButtons, _alphabet);
        // Вопрос из прежнего набора больше не подходит
        _target = null;
        _answered = true;
        foreach (var button in _answerButtons)
        {
            button.Text = "?";
            button.IsEnabled = false;
            ClearMark(button);
        }

        RepeatButton.IsEnabled = false;
        QuizStatusLabel.Text = Texts.T("Нажмите «Новый символ» и слушайте");
    }

    private async void NewButton_OnClicked(object? sender, EventArgs e)
    {
        var pool = LearningCatalog.GetItems(_alphabet);
        _target = pool[RandomNumberGenerator.GetInt32(pool.Count)];
        // Варианты с тем же кодом (А и A) не показываются: их на слух не различить
        var answers = pool.Where(item => item.Symbol != _target.Symbol && item.Code != _target.Code)
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))
            .Take(_answerButtons.Length - 1)
            .Append(_target)
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))
            .ToArray();
        for (var index = 0; index < _answerButtons.Length; index++)
        {
            var button = _answerButtons[index];
            ClearMark(button);
            button.IsEnabled = index < answers.Length;
            button.Text = index < answers.Length ? answers[index].Symbol.ToString() : string.Empty;
            button.CommandParameter = index < answers.Length ? answers[index].Symbol : null;
        }

        _answered = false;
        RepeatButton.IsEnabled = true;
        await PlayTargetAsync();
    }

    private async void RepeatButton_OnClicked(object? sender, EventArgs e) => await PlayTargetAsync();

    private async Task PlayTargetAsync()
    {
        if (_target is null)
        {
            return;
        }

        QuizStatusLabel.Text = Texts.T("Слушайте…");
        var settings = _settingsService.LoadSettings();
        var clip = MorseAudioService.Render(_target.Symbol.ToString(), 45, settings.FrequencyHz, settings.VolumePercent, 3, 7);
        try
        {
            var path = await AudioFileService.SaveClipAsync(clip, "quiz-signal.wav");
            _audioPlayback.Stop();
            await _audioPlayback.PlayAsync(path);
        }
        catch (OperationCanceledException)
        {
            // Другой звук остановил этот — не ошибка
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось воспроизвести"), exception.Message, Texts.T("Закрыть"));
        }

        if (!_answered)
        {
            QuizStatusLabel.Text = Texts.T("Какой символ прозвучал?");
        }
    }

    private void AnswerButton_OnClicked(object? sender, EventArgs e)
    {
        // Кнопки не выключаются после ответа: у выключенной кнопки не видно, где верный ответ
        if (_answered || _target is null || sender is not Button { CommandParameter: char answer } chosen)
        {
            return;
        }

        _answered = true;
        _total++;
        if (answer == _target.Symbol)
        {
            _correct++;
            QuizStatusLabel.Text = Texts.F("Верно: {0} — {1}", _target.Symbol, _target.Chant);
        }
        else
        {
            QuizStatusLabel.Text = Texts.F("Правильно: {0} — {1}", _target.Symbol, _target.Chant);
            chosen.BackgroundColor = (Color)Application.Current!.Resources["Danger"];
            chosen.TextColor = Colors.White;
        }

        foreach (var button in _answerButtons.Where(button => button.CommandParameter is char symbol && symbol == _target.Symbol))
        {
            button.BackgroundColor = (Color)Application.Current!.Resources["Primary"];
            button.TextColor = Color.FromArgb("#06231C");
        }

        SaveScore();
    }

    private void ResetScoreButton_OnClicked(object? sender, EventArgs e)
    {
        _correct = 0;
        _total = 0;
        SaveScore();
    }

    private void SaveScore()
    {
        var settings = _settingsService.LoadSettings();
        settings.QuizCorrect = _correct;
        settings.QuizTotal = _total;
        _settingsService.SaveSettings(settings);
        UpdateScore();
    }

    private void UpdateScore() => QuizScoreLabel.Text = Texts.F("Результат: {0} / {1}", _correct, _total);

    private static void ClearMark(Button button)
    {
        button.ClearValue(Button.BackgroundColorProperty);
        button.ClearValue(Button.TextColorProperty);
    }

    // Планшет или альбомная ориентация: контент полосой до 720 px по центру
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            RootLayout.Padding = TabletLayout.PaddingFor(width);
        }
    }
}
