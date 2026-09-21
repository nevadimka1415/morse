using System.Security.Cryptography;
using MorseTrainer.Domain;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class LearningPage : ContentPage
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private readonly VoicePackService _voicePack;
    private IReadOnlyList<LearningSymbolItem> _visibleItems = Array.Empty<LearningSymbolItem>();
    private LearningSymbolItem? _quizTarget;
    private int _quizCorrect;
    private int _quizTotal;
    private bool _pageReady;

    public LearningPage(
        IAudioPlaybackService audioPlayback,
        MobileSettingsService settingsService,
        VoicePackService voicePack)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _voicePack = voicePack;
        _pageReady = true;
        RefreshItems();
        var settings = _settingsService.LoadSettings();
        _quizCorrect = settings.QuizCorrect;
        _quizTotal = settings.QuizTotal;
        UpdateScore();
    }

    private void Filter_OnChanged(object sender, EventArgs e)
    {
        if (_pageReady)
        {
            RefreshItems();
        }
    }

    private void RefreshItems()
    {
        if (CardsView is null)
        {
            return;
        }

        var items = LearningCatalog.GetItems(Math.Clamp(AlphabetPicker?.SelectedIndex ?? 0, 0, 3));
        var search = SearchBox?.Text?.Trim() ?? string.Empty;
        _visibleItems = string.IsNullOrWhiteSpace(search)
            ? items
            : items.Where(item => item.Symbol.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || item.Chant.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        CardsView.ItemsSource = _visibleItems;
    }

    private async void VoiceButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: char symbol })
        {
            return;
        }

        var item = LearningCatalog.Find(symbol);
        var path = await _voicePack.GetVoiceFileAsync(symbol);
        if (path is null)
        {
            await DisplayAlertAsync("Голос недоступен", "Для этого символа не найден встроенный напев.", "Закрыть");
            return;
        }

        QuizStatusLabel.Text = $"{item?.Symbol}: {item?.Chant}";
        await PlayFileSafelyAsync(path);
    }

    private async void SignalButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: char symbol })
        {
            await PlayMorseAsync(symbol);
        }
    }

    private async Task PlayMorseAsync(char symbol)
    {
        var settings = _settingsService.LoadSettings();
        var clip = MorseAudioService.Render(symbol.ToString(), 45, settings.FrequencyHz, settings.VolumePercent, 3, 7);
        var path = await AudioFileService.SaveClipAsync(clip, "learning-signal.wav");
        await PlayFileSafelyAsync(path);
    }

    private async Task PlayFileSafelyAsync(string path)
    {
        try
        {
            _audioPlayback.Stop();
            await _audioPlayback.PlayAsync(path);
        }
        catch (OperationCanceledException)
        {
            // Starting another card intentionally stops the previous sound.
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Не удалось воспроизвести", exception.Message, "Закрыть");
        }
    }

    private async void QuizButton_OnClicked(object sender, EventArgs e)
    {
        var pool = _visibleItems.Count >= 4 ? _visibleItems : LearningCatalog.Russian;
        _quizTarget = pool[RandomNumberGenerator.GetInt32(pool.Count)];
        var answers = new List<LearningSymbolItem> { _quizTarget };
        answers.AddRange(pool.Where(item => item.Symbol != _quizTarget.Symbol && item.Code != _quizTarget.Code)
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(3));
        answers = answers.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).ToList();

        QuizAnswers.Children.Clear();
        foreach (var answer in answers)
        {
            var button = new Button
            {
                Text = answer.Symbol.ToString(),
                CommandParameter = answer.Symbol,
                MinimumWidthRequest = 64,
                Margin = 4
            };
            button.Clicked += QuizAnswer_OnClicked;
            QuizAnswers.Children.Add(button);
        }

        QuizStatusLabel.Text = "Слушайте…";
        await PlayMorseAsync(_quizTarget.Symbol);
        QuizStatusLabel.Text = "Какой символ прозвучал?";
    }

    private void QuizAnswer_OnClicked(object? sender, EventArgs e)
    {
        if (_quizTarget is null || sender is not Button { CommandParameter: char answer })
        {
            return;
        }

        _quizTotal++;
        if (answer == _quizTarget.Symbol)
        {
            _quizCorrect++;
            QuizStatusLabel.Text = $"Верно: {_quizTarget.Symbol} — {_quizTarget.Chant}";
        }
        else
        {
            QuizStatusLabel.Text = $"Правильно: {_quizTarget.Symbol} — {_quizTarget.Chant}";
        }

        foreach (var button in QuizAnswers.Children.OfType<Button>())
        {
            button.IsEnabled = false;
        }

        var settings = _settingsService.LoadSettings();
        settings.QuizCorrect = _quizCorrect;
        settings.QuizTotal = _quizTotal;
        _settingsService.SaveSettings(settings);
        UpdateScore();
    }

    private void UpdateScore()
    {
        QuizScoreLabel.Text = $"Результат: {_quizCorrect} / {_quizTotal}";
    }

    // Планшет или альбомная ориентация: центрируем контент полосой до 720 px
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            RootLayout.Padding = TabletLayout.PaddingFor(width, 14);
        }
        var columns = TabletLayout.LearningColumnsFor(width);
        if (CardsLayout.Span != columns)
        {
            CardsLayout.Span = columns;
        }
    }
}
