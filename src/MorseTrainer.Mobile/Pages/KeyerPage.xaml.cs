using System.Diagnostics;
using MorseTrainer.Domain;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class KeyerPage : ContentPage
{
    private readonly IAudioPlaybackService _audioPlayback;
    private readonly MobileSettingsService _settingsService;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private KeyerDecoder? _keyer;
    private int _keyerSpeed;
    private int _keyerAlphabet = -1;
    private bool _keyDown;
    private double _pressStart;
    private double _lastRelease = -1;
    private string? _tonePath;
    private int _toneFrequency;
    private int _toneVolume;
    private string _target = string.Empty;
    private bool _timerRunning;

    public KeyerPage(IAudioPlaybackService audioPlayback, MobileSettingsService settingsService)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnsureKeyer();
        if (!_timerRunning)
        {
            _timerRunning = true;
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(60), () =>
            {
                Tick();
                return _timerRunning;
            });
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timerRunning = false;
        if (_keyDown)
        {
            EndKeyPress();
        }
    }

    private void EnsureKeyer()
    {
        var settings = _settingsService.LoadSettings();
        var speed = Math.Clamp(settings.CharactersPerMinute, 20, 300);
        var alphabet = Math.Clamp(settings.AlphabetIndex, 0, 2);
        if (_keyer is null || _keyerSpeed != speed || _keyerAlphabet != alphabet)
        {
            _keyer = new KeyerDecoder((AlphabetMode)alphabet, speed);
            _keyerSpeed = speed;
            _keyerAlphabet = alphabet;
            TimingLabel.Text = $"Точка: {_keyer.UnitMilliseconds:0} мс · тире от {_keyer.UnitMilliseconds * KeyerDecoder.DashThresholdUnits:0} мс";
        }
    }

    private async void KeyButton_OnPressed(object sender, EventArgs e)
    {
        if (_keyDown)
        {
            return;
        }

        EnsureKeyer();
        _keyDown = true;
        var now = _clock.Elapsed.TotalMilliseconds;
        if (_lastRelease >= 0)
        {
            _keyer!.Idle(now - _lastRelease);
        }

        _pressStart = now;
        UpdateDisplay();
        try
        {
            await StartToneAsync();
        }
        catch
        {
            // Без звука ключ всё равно работает: декодер считает по времени нажатий
        }
    }

    private void KeyButton_OnReleased(object sender, EventArgs e) => EndKeyPress();

    private void EndKeyPress()
    {
        if (!_keyDown)
        {
            return;
        }

        _keyDown = false;
        _audioPlayback.Stop();
        var now = _clock.Elapsed.TotalMilliseconds;
        _keyer?.Press(now - _pressStart);
        _lastRelease = now;
        UpdateDisplay();
    }

    private async Task StartToneAsync()
    {
        var settings = _settingsService.LoadSettings();
        var frequency = Math.Clamp(settings.FrequencyHz, 300, 1200);
        var volume = Math.Clamp(settings.VolumePercent, 0, 100);
        if (_tonePath is null || _toneFrequency != frequency || _toneVolume != volume)
        {
            var clip = MorseAudioService.RenderTone(1, frequency, volume);
            _tonePath = await AudioFileService.SaveClipAsync(clip, "keyer-tone.wav");
            _toneFrequency = frequency;
            _toneVolume = volume;
        }

        if (_keyDown)
        {
            _audioPlayback.StartLoop(_tonePath);
        }
    }

    private void Tick()
    {
        if (_keyDown || _keyer is null || _lastRelease < 0)
        {
            return;
        }

        var before = _keyer.Text.Length + _keyer.PendingCode.Length;
        _keyer.Idle(_clock.Elapsed.TotalMilliseconds - _lastRelease);
        if (_keyer.Text.Length + _keyer.PendingCode.Length != before)
        {
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (_keyer is null)
        {
            return;
        }

        var pending = _keyer.PendingCode.Replace('.', '•').Replace('-', '—');
        CodeLabel.Text = pending.Length == 0 ? " " : pending;
        OutputLabel.Text = _keyer.Text.Length == 0 ? " " : _keyer.Text;
    }

    private void BackspaceButton_OnClicked(object sender, EventArgs e)
    {
        _keyer?.Backspace();
        UpdateDisplay();
    }

    private void ClearButton_OnClicked(object sender, EventArgs e)
    {
        _keyer?.Clear();
        _lastRelease = -1;
        UpdateDisplay();
    }

    private async void NewTaskButton_OnClicked(object sender, EventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        var alphabet = (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2);
        var content = (ContentMode)Math.Clamp(settings.ContentModeIndex, 0, 5);
        var pool = MorseAlphabet.BuildPool(alphabet, content, settings.CustomSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            await DisplayAlertAsync("Нет символов", "Откройте настройки и выберите хотя бы один символ.", "Понятно");
            return;
        }

        _target = TrainingGenerator.Generate(pool, Math.Clamp(Math.Min(settings.GroupCount, 3), 1, 3));
        TargetLabel.Text = _target;
        ResultLabel.Text = string.Empty;
        CheckButton.IsEnabled = true;
        ClearButton_OnClicked(sender, e);
    }

    private void CheckButton_OnClicked(object sender, EventArgs e)
    {
        if (_target.Length == 0 || _keyer is null)
        {
            return;
        }

        _keyer.CommitSymbol();
        UpdateDisplay();
        var result = TrainingEvaluator.Evaluate(_target, _keyer.Text);
        if (result.IsPerfect)
        {
            ResultLabel.Text = "Отлично: передано без ошибок.";
            ResultLabel.TextColor = (Color)Application.Current!.Resources["PrimaryDark"];
        }
        else
        {
            var details = result.Mistakes.Take(8).Select(mistake => $"{mistake.Position}: {mistake.Expected}→{mistake.Actual?.ToString() ?? "∅"}");
            ResultLabel.Text = $"Точность {result.AccuracyPercent:0.#}%. Ошибки: {string.Join(", ", details)}";
            ResultLabel.TextColor = (Color)Application.Current!.Resources["Danger"];
        }
    }
}
