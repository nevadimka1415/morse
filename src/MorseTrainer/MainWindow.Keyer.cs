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

/// <summary>Главное окно: вкладка «Передача»: ключ мышью или пробелом, задание, разбор качества.</summary>
public partial class MainWindow
{
    // ---------- Передача ключом ----------

    private void EnsureKeyer()
    {
        var speed = (int)SpeedSlider.Value;
        // Позывные и Q-код всегда декодируются латиницей
        var alphabet = (int)ContentModes.DecodingAlphabet(
            ContentModes.Clamp(ContentModeCombo.SelectedIndex), (AlphabetMode)Math.Clamp(AlphabetCombo.SelectedIndex, 0, 2));
        if (_keyer is null || _keyerSpeed != speed || _keyerAlphabet != alphabet)
        {
            var text = _keyer?.Text ?? string.Empty;
            _keyer = new KeyerDecoder((AlphabetMode)alphabet, speed);
            _keyerSpeed = speed;
            _keyerAlphabet = alphabet;
            KeyerTimingText.Text = Texts.F("Точка: {0:0} мс · тире от {1:0} мс", _keyer.UnitMilliseconds, _keyer.UnitMilliseconds * KeyerDecoder.DashThresholdUnits);
            if (text.Length > 0)
            {
                KeyerOutputText.Text = text;
            }
        }

        if (_keyerTimer is null)
        {
            _keyerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _keyerTimer.Tick += KeyerTimer_OnTick;
            _keyerTimer.Start();
        }
    }

    private void StartKeyPress()
    {
        // В режиме «Приём с микрофона» экранный ключ не работает — передача идёт настоящим ключом
        if (_keyDown || MicMode)
        {
            return;
        }

        EnsureKeyer();
        _keyDown = true;
        var now = _keyerClock.Elapsed.TotalMilliseconds;
        if (_keyerLastRelease >= 0)
        {
            _keyer!.Idle(now - _keyerLastRelease);
        }

        _keyerPressStart = now;
        StartKeyerTone();
        UpdateKeyerDisplay();
    }

    private void EndKeyPress()
    {
        if (!_keyDown)
        {
            return;
        }

        _keyDown = false;
        _tonePlayer?.Stop();
        var now = _keyerClock.Elapsed.TotalMilliseconds;
        _keyer?.Press(now - _keyerPressStart);
        _keyerLastRelease = now;
        UpdateKeyerDisplay();
    }

    private void KeyerTimer_OnTick(object? sender, EventArgs e)
    {
        if (_keyDown || _keyer is null || _keyerLastRelease < 0)
        {
            return;
        }

        var before = _keyer.Text.Length + _keyer.PendingCode.Length;
        _keyer.Idle(_keyerClock.Elapsed.TotalMilliseconds - _keyerLastRelease);
        if (_keyer.Text.Length + _keyer.PendingCode.Length != before || _keyer.PendingCode.Length == 0)
        {
            UpdateKeyerDisplay();
        }
    }

    private void StartKeyerTone()
    {
        var frequency = (int)FrequencySlider.Value;
        var volume = (int)VolumeSlider.Value;
        if (_tonePlayer is null || _toneFrequency != frequency || _toneVolume != volume)
        {
            StopKeyerTone();
            _toneStream = new MemoryStream(MorseAudioService.RenderTone(1, frequency, volume).WavBytes);
            _tonePlayer = new SoundPlayer(_toneStream);
            _tonePlayer.Load();
            _toneFrequency = frequency;
            _toneVolume = volume;
        }

        _tonePlayer.PlayLooping();
    }

    private void StopKeyerTone()
    {
        _tonePlayer?.Stop();
        _tonePlayer?.Dispose();
        _tonePlayer = null;
        _toneStream?.Dispose();
        _toneStream = null;
    }

    private void UpdateKeyerDisplay()
    {
        if (_keyer is null)
        {
            return;
        }

        var pending = _keyer.PendingCode.Replace('.', '•').Replace('-', '—');
        KeyerCodeText.Text = pending.Length == 0 ? " " : pending;
        KeyerOutputText.Text = _keyer.Text;
        KeyerOutputText.CaretIndex = KeyerOutputText.Text.Length;
        UpdateKeyerTarget();
    }

    /// <summary>Задание в «Передаче»: уже переданное верно подряд с начала подсвечено, остальное приглушено.</summary>
    private void UpdateKeyerTarget()
    {
        KeyerTargetText.Inlines.Clear();
        if (_keyerTarget.Length == 0)
        {
            return;
        }

        var matched = KeyerProgress.MatchedLength(_keyerTarget, _keyer?.Text);
        if (matched > 0)
        {
            var done = new Run(_keyerTarget[..matched]) { FontWeight = FontWeights.Bold };
            done.SetResourceReference(TextElement.ForegroundProperty, "PrimaryBrush");
            KeyerTargetText.Inlines.Add(done);
        }

        if (matched < _keyerTarget.Length)
        {
            KeyerTargetText.Inlines.Add(new Run(_keyerTarget[matched..]));
        }
    }

    private void KeyPad_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        KeyPad.CaptureMouse();
        StartKeyPress();
        e.Handled = true;
    }

    private void KeyPad_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        KeyPad.ReleaseMouseCapture();
        EndKeyPress();
        e.Handled = true;
    }

    private void KeyPad_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_keyDown && !KeyPad.IsMouseCaptured)
        {
            EndKeyPress();
        }
    }

    private void KeyPad_OnLostMouseCapture(object sender, MouseEventArgs e) => EndKeyPress();

    private void KeyerModeCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (KeyerNewTaskButton is null || KeyerCheckButton is null || KeyerTargetText is null)
        {
            return;
        }

        var taskMode = KeyerModeCombo.SelectedIndex == 1;
        KeyerNewTaskButton.Visibility = taskMode ? Visibility.Visible : Visibility.Collapsed;
        KeyerCheckButton.Visibility = taskMode ? Visibility.Visible : Visibility.Collapsed;
        // «Приём с микрофона»: слева — управление, справа вместо ключа — тон, скорость и уровень
        MicPanel.Visibility = MicMode ? Visibility.Visible : Visibility.Collapsed;
        MicPad.Visibility = MicMode ? Visibility.Visible : Visibility.Collapsed;
        KeyPad.Visibility = MicMode ? Visibility.Collapsed : Visibility.Visible;
        KeyerBackspaceButton.Visibility = MicMode ? Visibility.Collapsed : Visibility.Visible;
        if (MicMode)
        {
            HighlightChoice(new[] { MicRussianButton, MicLatinButton }, _micAlphabet);
            UpdateMicDisplay();
        }
        else
        {
            StopMicrophone();
            UpdateKeyerDisplay();
        }
        if (!taskMode)
        {
            _keyerTarget = string.Empty;
            KeyerTargetText.Text = string.Empty;
            KeyerResultText.Text = string.Empty;
        }
        else if (_keyerTarget.Length == 0)
        {
            KeyerNewTaskButton_OnClick(sender, e);
        }
    }

    private void KeyerNewTaskButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings();
        var alphabet = (AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2);
        var content = ContentModes.Clamp(settings.ContentModeIndex);
        var pool = MorseAlphabet.BuildPool(alphabet, content, _selectedSymbols, settings.KochLevel);
        if (pool.Count == 0)
        {
            KeyerResultText.Text = Texts.T("В выбранном наборе нет символов: измените состав задания на вкладке «Тренировка».");
            return;
        }

        _keyerTarget = TrainingGenerator.GenerateTask(content, alphabet, pool, Math.Clamp(Math.Min(settings.GroupCount, 4), 1, 4));
        UpdateKeyerTarget();
        KeyerResultText.Text = Texts.T("Передайте текст выше, затем нажмите «Проверить передачу».");
        KeyerResultText.Foreground = (Brush)FindResource("MutedTextBrush");
        KeyerClearButton_OnClick(sender, e);
    }

    private void KeyerCheckButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_keyerTarget.Length == 0 || _keyer is null)
        {
            return;
        }

        _keyer.CommitSymbol();
        UpdateKeyerDisplay();
        var result = TrainingEvaluator.Evaluate(_keyerTarget, _keyer.Text);
        if (result.IsPerfect)
        {
            KeyerResultText.Text = Texts.T("Отлично: передано без ошибок.");
            KeyerResultText.Foreground = (Brush)FindResource("PrimaryBrush");
        }
        else
        {
            var details = result.Mistakes.Take(8).Select(mistake => $"{mistake.Position}: {mistake.Expected}→{mistake.Actual?.ToString() ?? "∅"}");
            KeyerResultText.Text = Texts.F("Точность {0:0.#}%. Ошибки: {1}{2}", result.AccuracyPercent, string.Join(", ", details), result.Mistakes.Count > 8 ? " …" : string.Empty);
            KeyerResultText.Foreground = (Brush)FindResource("DangerBrush");
        }

        ShowKeyerAnalysis();
    }

    private void KeyerAnalyzeButton_OnClick(object sender, RoutedEventArgs e) => ShowKeyerAnalysis();

    private void ShowKeyerAnalysis()
    {
        if (KeyerAnalysisText is null)
        {
            return;
        }

        // «Разбор» — по тому, что сейчас в поле: экранный ключ или приём с микрофона
        var analysis = MicMode ? _micDecoder?.Analyze() : _keyer?.Analyze();
        if (analysis is null)
        {
            return;
        }

        KeyerAnalysisText.Text = analysis.Describe();
        KeyerAnalysisText.Foreground = (Brush)FindResource(analysis.HasEnoughData ? "TextBrush" : "MutedTextBrush");
    }

    private void KeyerBackspaceButton_OnClick(object sender, RoutedEventArgs e)
    {
        _keyer?.Backspace();
        UpdateKeyerDisplay();
    }

    private void KeyerClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (MicMode)
        {
            _micDecoder?.Clear();
            UpdateMicDisplay();
            return;
        }

        _keyer?.Clear();
        _keyerLastRelease = -1;
        UpdateKeyerDisplay();
        if (KeyerCodeText is not null)
        {
            KeyerCodeText.Text = " ";
        }
    }

    // ---------- Приём с микрофона: настоящий ключ, рация, приёмник ----------

    private bool MicMode => KeyerModeCombo?.SelectedIndex == 2;

    private AlphabetMode MicAlphabetMode => _micAlphabet == 1 ? AlphabetMode.Latin : AlphabetMode.Russian;

    private void MicButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_microphone?.IsRunning == true)
        {
            StopMicrophone();
            return;
        }

        StartMicrophone();
    }

    internal void StartMicrophone()
    {
        _microphone ??= new MicrophoneCapture();
        try
        {
            // Звук приходит из фонового потока в текущий декодер (при смене алфавита он заменяется)
            _micDecoder = new MorseAudioDecoder(MicrophoneCapture.SampleRate, MicAlphabetMode);
            _microphone.Start((samples, count) => _micDecoder?.Process(samples.AsSpan(0, count)));
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            _microphone.Stop();
            MicStatusText.Text = Texts.F("Микрофон недоступен: {0}", exception.Message);
            return;
        }

        MicButton.Content = Texts.T("■ Остановить");
        if (_micTimer is null)
        {
            _micTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _micTimer.Tick += (_, _) => UpdateMicDisplay();
        }

        _micTimer.Start();
        UpdateMicDisplay();
    }

    private void StopMicrophone()
    {
        if (_microphone?.IsRunning != true)
        {
            return;
        }

        _microphone.Stop();
        _micTimer?.Stop();
        _micDecoder?.Flush();
        if (MicButton is not null)
        {
            MicButton.Content = Texts.T("● Слушать микрофон");
            UpdateMicDisplay();
        }
    }

    private void UpdateMicDisplay()
    {
        var listening = _microphone?.IsRunning == true;
        if (_micDecoder is not { } decoder)
        {
            MicLevelBar.Value = 0;
            if (MicMode)
            {
                KeyerOutputText.Text = string.Empty;
                KeyerCodeText.Text = " ";
            }

            return;
        }

        var tone = decoder.ToneHz;
        var speed = decoder.CharactersPerMinute;
        MicStatusText.Text = !listening ? Texts.T("Остановлено")
            : tone == 0 ? Texts.T("Слушаю… ищу тон")
            : speed == 0 ? Texts.F("Тон {0} Гц", tone)
            : Texts.F("Тон {0} Гц · {1} зн/мин", tone, speed);
        MicLevelBar.Value = listening ? decoder.Level : 0;
        if (!MicMode)
        {
            return;
        }

        var pending = decoder.PendingCode.Replace('.', '•').Replace('-', '—');
        KeyerCodeText.Text = pending.Length == 0 ? " " : pending;
        KeyerOutputText.Text = decoder.Text;
        KeyerOutputText.CaretIndex = KeyerOutputText.Text.Length;
    }

    private void MicAlphabetButton_OnClick(object sender, RoutedEventArgs e)
    {
        var index = ChoiceIndex(sender, 1);
        if (index < 0)
        {
            return;
        }

        _micAlphabet = index;
        HighlightChoice(new[] { MicRussianButton, MicLatinButton }, _micAlphabet);
        // Слушаем — новый декодер с другим алфавитом (текст начинается заново)
        if (_microphone?.IsRunning == true)
        {
            _micDecoder = new MorseAudioDecoder(MicrophoneCapture.SampleRate, MicAlphabetMode);
            UpdateMicDisplay();
        }
    }

    /// <summary>Для проверки без микрофона: звук прогоняется через декодер режима «Приём с микрофона».</summary>
    internal void DecodeMicrophoneSamples(float[] samples, int sampleRate)
    {
        _micDecoder = new MorseAudioDecoder(sampleRate, MicAlphabetMode);
        _micDecoder.Process(samples);
        _micDecoder.Flush();
        UpdateMicDisplay();
    }
}