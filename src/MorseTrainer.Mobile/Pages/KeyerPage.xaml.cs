using System.Diagnostics;
using Microsoft.Maui.Devices;
using MorseTrainer.Domain;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile.Pages;

public partial class KeyerPage : ContentPage, IDisposable
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
    private const string MicAlphabetKey = "keyer.mic_alphabet";
    private readonly IMicrophoneStream _microphone;
    private readonly Button[] _micAlphabetButtons;
    private int _micAlphabet;
    private int _micSampleRate;
    private volatile MorseAudioDecoder? _micDecoder;
    private bool _micTimerRunning;

    public KeyerPage(IAudioPlaybackService audioPlayback, MobileSettingsService settingsService, IMicrophoneStream microphone)
    {
        InitializeComponent();
        _audioPlayback = audioPlayback;
        _settingsService = settingsService;
        _microphone = microphone;
        _micAlphabetButtons = new[] { MicRussianButton, MicLatinButton };
        _micAlphabet = Math.Clamp(Preferences.Default.Get(MicAlphabetKey, 0), 0, 1);
        ChoiceButtons.Highlight(_micAlphabetButtons, _micAlphabet);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Приложение свернули — микрофон отпускается (OnDisappearing при этом не вызывается)
        if (Window is { } window)
        {
            window.Stopped -= Window_OnStopped;
            window.Stopped += Window_OnStopped;
        }

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

    // Окно закрыто (Android пересоздал активность): таймер и тон ключа останавливаются без обновления элементов
    public void Dispose()
    {
        _timerRunning = false;
        _keyDown = false;
        _audioPlayback.Stop();
        _micTimerRunning = false;
        _microphone.Stop();
    }

    private void Window_OnStopped(object? sender, EventArgs e)
    {
        if (_microphone.IsRunning)
        {
            StopMicrophone();
        }

        if (_keyDown)
        {
            EndKeyPress();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Window is { } window)
        {
            window.Stopped -= Window_OnStopped;
        }

        _timerRunning = false;
        // Ушли с вкладки — микрофон отпускается
        if (_microphone.IsRunning)
        {
            StopMicrophone();
        }

        if (_keyDown)
        {
            EndKeyPress();
        }
    }

    private void EnsureKeyer()
    {
        var settings = _settingsService.LoadSettings();
        var speed = Math.Clamp(settings.CharactersPerMinute, 20, 300);
        // Позывные и Q-код всегда декодируются латиницей
        var alphabet = (int)ContentModes.DecodingAlphabet(
            ContentModes.Clamp(settings.ContentModeIndex), (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2));
        if (_keyer is null || _keyerSpeed != speed || _keyerAlphabet != alphabet)
        {
            _keyer = new KeyerDecoder((AlphabetMode)alphabet, speed);
            _keyerSpeed = speed;
            _keyerAlphabet = alphabet;
            TimingLabel.Text = Texts.F("Точка: {0:0} мс · тире от {1:0} мс", _keyer.UnitMilliseconds, _keyer.UnitMilliseconds * KeyerDecoder.DashThresholdUnits);
        }
    }

    private async void KeyButton_OnPressed(object sender, EventArgs e)
    {
        // Пока слушаем микрофон, экранный ключ молчит: его тон попал бы в микрофон, а на iPhone воспроизведение
        // переключает звук телефона и глушит запись
        if (_keyDown || _microphone.IsRunning)
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
        UpdateTarget();
    }

    /// <summary>Задание: уже переданное верно подряд с начала подсвечено цветом.</summary>
    private void UpdateTarget()
    {
        var matched = KeyerProgress.MatchedLength(_target, _keyer?.Text);
        var text = new FormattedString();
        if (matched > 0)
        {
            text.Spans.Add(new Span { Text = _target[..matched], TextColor = (Color)Application.Current!.Resources["PrimaryDark"] });
        }

        if (matched < _target.Length)
        {
            text.Spans.Add(new Span { Text = _target[matched..] });
        }

        TargetLabel.FormattedText = text;
    }

    private void AnalyzeButton_OnClicked(object sender, EventArgs e) => ShowAnalysis();

    private void ShowAnalysis()
    {
        if (_keyer is null)
        {
            return;
        }

        var analysis = _keyer.Analyze();
        AnalysisLabel.Text = analysis.Describe();
        AnalysisLabel.Opacity = analysis.HasEnoughData ? 1 : 0.65;
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
        var content = ContentModes.Clamp(settings.ContentModeIndex);
        var pool = MorseAlphabet.BuildPool(alphabet, content, settings.CustomSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            await DisplayAlertAsync(Texts.T("Нет символов"), Texts.T("Откройте настройки и выберите хотя бы один символ."), Texts.T("Понятно"));
            return;
        }

        _target = TrainingGenerator.GenerateTask(content, alphabet, pool, Math.Clamp(Math.Min(settings.GroupCount, 3), 1, 3));
        EnsureKeyer();
        UpdateTarget();
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
            ResultLabel.Text = Texts.T("Отлично: передано без ошибок.");
            ResultLabel.TextColor = (Color)Application.Current!.Resources["PrimaryDark"];
        }
        else
        {
            var details = result.Mistakes.Take(8).Select(mistake => $"{mistake.Position}: {mistake.Expected}→{mistake.Actual?.ToString() ?? "∅"}");
            ResultLabel.Text = Texts.F("Точность {0:0.#}%. Ошибки: {1}", result.AccuracyPercent, string.Join(", ", details));
            ResultLabel.TextColor = (Color)Application.Current!.Resources["Danger"];
        }

        ShowAnalysis();
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

    // ---------- Приём с микрофона: настоящий ключ, рация, приёмник ----------

    private AlphabetMode MicAlphabetMode => _micAlphabet == 1 ? AlphabetMode.Latin : AlphabetMode.Russian;

    private async void MicButton_OnClicked(object sender, EventArgs e)
    {
        if (_microphone.IsRunning)
        {
            StopMicrophone();
            return;
        }

        if (!await _microphone.RequestPermissionAsync())
        {
            await DisplayAlertAsync(Texts.T("Приём с микрофона"),
                Texts.T("Нет доступа к микрофону: разрешите его для Morse Trainer в настройках телефона."), Texts.T("Понятно"));
            return;
        }

        if (_keyDown)
        {
            EndKeyPress();
        }

        try
        {
            // Звук приходит из фонового потока в текущий декодер (при смене алфавита он заменяется)
            _micSampleRate = _microphone.Start((samples, count) => _micDecoder?.Process(samples.AsSpan(0, count)));
            _micDecoder = new MorseAudioDecoder(_micSampleRate, MicAlphabetMode);
        }
        catch (Exception exception)
        {
            _microphone.Stop();
            await DisplayAlertAsync(Texts.T("Приём с микрофона"), Texts.F("Микрофон недоступен: {0}", exception.Message), Texts.T("Понятно"));
            return;
        }

        MicButton.Text = Texts.T("■ Остановить");
        MicAnalysisLabel.IsVisible = false;
        KeyButton.IsEnabled = false;
        // Пока слушаем, экран не гаснет: текст читают во время передачи
        DeviceDisplay.Current.KeepScreenOn = true;
        UpdateMicrophone();
        if (!_micTimerRunning)
        {
            _micTimerRunning = true;
            Dispatcher.StartTimer(TimeSpan.FromMilliseconds(150), () =>
            {
                UpdateMicrophone();
                return _micTimerRunning;
            });
        }
    }

    private void StopMicrophone()
    {
        _microphone.Stop();
        _micTimerRunning = false;
        _micDecoder?.Flush();
        MicButton.Text = Texts.T("● Слушать микрофон");
        KeyButton.IsEnabled = true;
        DeviceDisplay.Current.KeepScreenOn = false;
        UpdateMicrophone();
    }

    private void UpdateMicrophone()
    {
        if (_micDecoder is not { } decoder)
        {
            return;
        }

        // Микрофон отобрала система (звонок, другое приложение) — кнопка и экран возвращаются в «остановлено»
        if (_micTimerRunning && !_microphone.IsRunning)
        {
            StopMicrophone();
            return;
        }

        var tone = decoder.ToneHz;
        var speed = decoder.CharactersPerMinute;
        MicStatusLabel.Text = !_microphone.IsRunning ? Texts.T("Остановлено")
            : tone == 0 ? Texts.T("Слушаю… ищу тон")
            : speed == 0 ? Texts.F("Тон {0} Гц", tone)
            : Texts.F("Тон {0} Гц · {1} зн/мин", tone, speed);
        MicLevelBar.Progress = _microphone.IsRunning ? decoder.Level : 0;
        // Незаконченный знак — точками и тире после текста
        var pending = decoder.PendingCode.Replace('.', '·').Replace('-', '–');
        var shown = decoder.Text + (pending.Length > 0 ? " " + pending : string.Empty);
        MicTextLabel.Text = shown.Length > 0 ? shown : " ";
    }

    private void MicClearButton_OnClicked(object sender, EventArgs e)
    {
        _micDecoder?.Clear();
        MicAnalysisLabel.IsVisible = false;
        UpdateMicrophone();
    }

    // Разбор ручной передачи по принятому с микрофона — как у экранного ключа
    private void MicAnalyzeButton_OnClicked(object sender, EventArgs e)
    {
        if (_micDecoder is not { } decoder)
        {
            return;
        }

        var analysis = decoder.Analyze();
        MicAnalysisLabel.Text = analysis.Describe();
        MicAnalysisLabel.Opacity = analysis.HasEnoughData ? 1 : 0.65;
        MicAnalysisLabel.IsVisible = true;
    }

    private void MicAlphabetButton_OnClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string parameter } || !int.TryParse(parameter, out var index))
        {
            return;
        }

        _micAlphabet = Math.Clamp(index, 0, 1);
        Preferences.Default.Set(MicAlphabetKey, _micAlphabet);
        ChoiceButtons.Highlight(_micAlphabetButtons, _micAlphabet);
        // Слушаем — новый декодер с другим алфавитом (текст начинается заново)
        if (_microphone.IsRunning)
        {
            _micDecoder = new MorseAudioDecoder(_micSampleRate, MicAlphabetMode);
            UpdateMicrophone();
        }
    }
}