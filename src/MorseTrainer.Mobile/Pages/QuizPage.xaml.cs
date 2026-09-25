using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Pages;

/// <summary>Проверка на слух: звучит символ, из четырёх вариантов нужно выбрать прозвучавший.</summary>
public partial class QuizPage : ContentPage, IDisposable
{
    private const string AlphabetKey = "quiz.alphabet";

    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private readonly Button[] _alphabetButtons;
    private readonly Button[] _answerButtons;
    private LearningSymbolItem? _target;
    private bool _answered = true;
    private bool _visible;
    private int _correct;
    private int _total;
    private int _alphabet;
    private int _speed;
    private bool _autoNext;
    private bool _ready;
    private CancellationTokenSource? _nextCancellation;

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
        _speed = EarQuiz.ClampSpeed(settings.QuizSpeed);
        _autoNext = settings.QuizAutoNext;
        SpeedSlider.Value = _speed;
        AutoNextSwitch.IsToggled = _autoNext;
        UpdateSpeedLabel();
        UpdateScore();
        // До этой строки ползунок и переключатель только принимают сохранённые значения, настройки не пишутся
        _ready = true;
    }

    // Окно закрыто (Android пересоздал активность): следующий символ не должен прозвучать со старой страницы
    public void Dispose()
    {
        CancelScheduledNext();
        _visible = false;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
    }

    // Ушли на другую вкладку — автопереход останавливается, чтобы символы не звучали поверх другой страницы
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        CancelScheduledNext();
    }

    private void AlphabetButton_OnClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string parameter } || !int.TryParse(parameter, out var index))
        {
            return;
        }

        CancelScheduledNext();
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

    private async void NewButton_OnClicked(object? sender, EventArgs e) => await AskNextAsync();

    private async Task AskNextAsync()
    {
        CancelScheduledNext();
        var question = EarQuiz.Next(LearningCatalog.GetItems(_alphabet), _target?.Symbol);
        _target = question.Target;
        for (var index = 0; index < _answerButtons.Length; index++)
        {
            var button = _answerButtons[index];
            var answer = index < question.Answers.Count ? question.Answers[index] : null;
            ClearMark(button);
            button.IsEnabled = answer is not null;
            button.Text = answer?.Symbol.ToString() ?? string.Empty;
            button.CommandParameter = answer?.Symbol;
        }

        _answered = false;
        RepeatButton.IsEnabled = true;
        await PlayTargetAsync();
    }

    private async void RepeatButton_OnClicked(object? sender, EventArgs e)
    {
        // Повтор после ответа (например, послушать верный символ после ошибки): следующий ждёт конца повтора
        var waitingForNext = CancelScheduledNext();
        await PlayTargetAsync();
        if (waitingForNext && _answered)
        {
            await ScheduleNextAsync(correct: true);
        }
    }

    private async Task PlayTargetAsync()
    {
        if (_target is null)
        {
            return;
        }

        QuizStatusLabel.Text = Texts.T("Слушайте…");
        var settings = _settingsService.LoadSettings();
        var clip = MorseAudioService.Render(_target.Symbol.ToString(), _speed, settings.FrequencyHz, settings.VolumePercent, 3, 7);
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

    private async void AnswerButton_OnClicked(object? sender, EventArgs e)
    {
        // Кнопки не выключаются после ответа: у выключенной кнопки не видно, где верный ответ
        if (_answered || _target is null || sender is not Button { CommandParameter: char answer } chosen)
        {
            return;
        }

        _answered = true;
        _total++;
        var correct = answer == _target.Symbol;
        if (correct)
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
        await ScheduleNextAsync(correct);
    }

    /// <summary>Следующий символ после паузы, если включён автопереход и вкладка открыта.</summary>
    private async Task ScheduleNextAsync(bool correct)
    {
        if (!_autoNext || !_visible)
        {
            return;
        }

        CancelScheduledNext();
        var cancellation = new CancellationTokenSource();
        _nextCancellation = cancellation;
        try
        {
            await Task.Delay(EarQuiz.NextDelay(correct), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(_nextCancellation, cancellation) || !_visible || !_autoNext)
        {
            return;
        }

        _nextCancellation = null;
        cancellation.Dispose();
        await AskNextAsync();
    }

    /// <summary>Отменяет ожидающий следующий символ; true — он действительно ждал.</summary>
    private bool CancelScheduledNext()
    {
        var pending = _nextCancellation;
        if (pending is null)
        {
            return false;
        }

        _nextCancellation = null;
        pending.Cancel();
        pending.Dispose();
        return true;
    }

    private void SpeedSlider_OnValueChanged(object? sender, ValueChangedEventArgs e)
    {
        var speed = EarQuiz.ClampSpeed((int)Math.Round(e.NewValue));
        if (!_ready || speed == _speed)
        {
            return;
        }

        _speed = speed;
        UpdateSpeedLabel();
        var settings = _settingsService.LoadSettings();
        settings.QuizSpeed = _speed;
        _settingsService.SaveSettings(settings);
    }

    private void AutoNextSwitch_OnToggled(object? sender, ToggledEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        _autoNext = e.Value;
        if (!_autoNext)
        {
            CancelScheduledNext();
        }

        var settings = _settingsService.LoadSettings();
        settings.QuizAutoNext = _autoNext;
        _settingsService.SaveSettings(settings);
    }

    private void UpdateSpeedLabel() => SpeedValueLabel.Text = Texts.F("{0} знаков/мин", _speed);

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
