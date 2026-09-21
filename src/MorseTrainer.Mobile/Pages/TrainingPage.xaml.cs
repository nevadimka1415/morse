using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class TrainingPage : ContentPage
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private CancellationTokenSource? _playbackCancellation;
    private AudioClip? _currentClip;
    private string _currentTask = string.Empty;
    private bool _answerVisible;

    public TrainingPage(IAudioPlaybackService audioPlayback, MobileSettingsService settingsService)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _settingsService.SettingsChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshSettingsSummary);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        RefreshSettingsSummary();
        if (string.IsNullOrEmpty(_currentTask))
        {
            await GenerateTaskAsync();
        }
    }

    private void RefreshSettingsSummary()
    {
        var settings = _settingsService.LoadSettings();
        ProfileLabel.Text = $"Профиль: {settings.ActiveProfileName}";
        var start = settings.PlayStartSignal ? $" · Ж Ж Ж + пауза {settings.StartPauseUnits}" : " · без сигнала старта";
        SettingsSummaryLabel.Text = $"{settings.GroupCount} групп × 5 · {settings.CharactersPerMinute} знаков/мин · " +
                                    $"{settings.FrequencyHz} Гц · паузы {settings.CharacterGapUnits}/{settings.GroupGapUnits}{start}";
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
        var settings = _settingsService.LoadSettings();
        var alphabet = (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2);
        var content = (ContentMode)Math.Clamp(settings.ContentModeIndex, 0, 4);
        var pool = MorseAlphabet.BuildPool(alphabet, content, settings.CustomSymbols);
        if (pool.Count == 0)
        {
            await DisplayAlertAsync("Нет символов", "Откройте настройки и выберите хотя бы один символ.", "Понятно");
            return;
        }

        GenerateButton.IsEnabled = false;
        PlaybackStatusLabel.Text = "Создаём задание…";
        try
        {
            _currentTask = TrainingGenerator.Generate(pool, Math.Clamp(settings.GroupCount, 1, 100));
            _currentClip = await Task.Run(() => MorseAudioService.Render(
                _currentTask,
                Math.Clamp(settings.CharactersPerMinute, 20, 300),
                Math.Clamp(settings.FrequencyHz, 300, 1200),
                Math.Clamp(settings.VolumePercent, 0, 100),
                Math.Clamp(settings.CharacterGapUnits, 3, 20),
                Math.Clamp(settings.GroupGapUnits, 7, 30),
                settings.PlayStartSignal,
                Math.Clamp(settings.StartPauseUnits, 7, 60)));
            _answerVisible = false;
            AnswerEditor.Text = string.Empty;
            AccuracyLabel.Text = "Точность: —";
            ResultLabel.Text = "Пробелы между группами не учитываются";
            UpdateTaskLabel();
            PlayButton.IsEnabled = true;
            RepeatButton.IsEnabled = true;
            CheckButton.IsEnabled = true;
            PlaybackStatusLabel.Text = $"Готово · {_currentClip.Duration:mm\\:ss}";
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Ошибка", exception.Message, "Закрыть");
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private async void PlayButton_OnClicked(object sender, EventArgs e)
    {
        if (_currentClip is null)
        {
            return;
        }

        StopPlayback();
        _playbackCancellation = new CancellationTokenSource();
        PlayButton.IsEnabled = false;
        RepeatButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        PlaybackStatusLabel.Text = "Сначала Ж Ж Ж, затем начнётся задание…";
        try
        {
            var path = await AudioFileService.SaveClipAsync(_currentClip, "current-training.wav", _playbackCancellation.Token);
            await _audioPlayback.PlayAsync(path, _playbackCancellation.Token);
            PlaybackStatusLabel.Text = "Готово — введите ответ";
            AnswerEditor.Focus();
        }
        catch (OperationCanceledException)
        {
            PlaybackStatusLabel.Text = "Воспроизведение остановлено";
        }
        finally
        {
            _playbackCancellation?.Dispose();
            _playbackCancellation = null;
            PlayButton.IsEnabled = _currentClip is not null;
            RepeatButton.IsEnabled = _currentClip is not null;
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
        ToggleAnswerButton.Text = _answerVisible ? "Скрыть" : "Показать";
    }

    private void CheckButton_OnClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_currentTask))
        {
            return;
        }

        var result = TrainingEvaluator.Evaluate(_currentTask, AnswerEditor.Text ?? string.Empty);
        AccuracyLabel.Text = $"Точность: {result.AccuracyPercent:0.#}%";
        if (result.IsPerfect)
        {
            ResultLabel.Text = "Отлично: все символы распознаны правильно";
            ResultLabel.TextColor = (Color)Application.Current!.Resources["PrimaryDark"];
        }
        else
        {
            ResultLabel.Text = "Ошибки: " + string.Join(", ", result.Mistakes.Take(8)
                .Select(FormatMistake));
            ResultLabel.TextColor = (Color)Application.Current!.Resources["Danger"];
        }

        _answerVisible = true;
        UpdateTaskLabel();
    }

    private static string FormatMistake(CharacterMistake mistake)
    {
        var actual = mistake.Actual?.ToString() ?? "∅";
        return $"{mistake.Position}: {mistake.Expected}→{actual}";
    }
}
