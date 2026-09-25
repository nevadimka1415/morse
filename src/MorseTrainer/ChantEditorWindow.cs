using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Services;

namespace MorseTrainer;

/// <summary>
/// Окно «Изменить напев»: свой напев для символа с проверкой числа слогов на лету и свой голос — запись с микрофона.
/// Result — новый напев или пустая строка, если нажато «Встроенный» (вернуть встроенный).
/// VoiceChanged — запись голоса сделана или удалена (карточкам нужно обновить пометку «ваш голос»).
/// </summary>
public sealed class ChantEditorWindow : Window
{
    private readonly char _symbol;
    private readonly TextBox _chantBox;
    private readonly TextBlock _statusText;
    private readonly Button _saveButton;
    // Свой голос: запись с микрофона в папку голоса (code_XXXX.wav), прослушивание, удаление
    private readonly string _voicePath;
    private readonly VoiceRecorder _recorder = new();
    private readonly DispatcherTimer _recordLimit = new() { Interval = TimeSpan.FromSeconds(MaxRecordSeconds) };
    private readonly TextBlock _voiceStatus;
    private readonly Button _recordButton;
    private readonly Button _listenButton;
    private readonly Button _deleteVoiceButton;
    private SoundPlayer? _player;
    private const int MaxRecordSeconds = 6;

    public ChantEditorWindow(LearningSymbolItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _symbol = item.Symbol;
        Title = Texts.F("Напев для {0}", item.Symbol);
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");

        var builtIn = LearningCatalog.BuiltInChant(item.Symbol) ?? string.Empty;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = $"{item.Symbol}    {item.Code}", FontSize = 28, FontWeight = FontWeights.Bold });
        panel.Children.Add(Muted(Texts.F("Ритм: {0}. Один слог на каждую точку и тире через дефис; для тире — протяжный слог с долгой гласной («даа»).", ChantBook.Pattern(item.Code))));
        panel.Children.Add(Muted(Texts.F("Встроенный напев: {0}", builtIn)));

        _chantBox = new TextBox { Text = item.Chant, FontSize = 18, Margin = new Thickness(0, 14, 0, 6), Padding = new Thickness(6, 4, 6, 4) };
        _chantBox.TextChanged += (_, _) => Validate();
        panel.Children.Add(_chantBox);
        _statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, MinHeight = 30 };
        panel.Children.Add(_statusText);

        // Свой голос: напев своим голосом вместо встроенного
        _voicePath = Path.Combine(AppPaths.VoiceDirectory, CustomVoice.ExampleFileName(item.Symbol));
        var voicePanel = new StackPanel();
        voicePanel.Children.Add(new TextBlock { Text = Texts.T("Свой голос"), FontWeight = FontWeights.SemiBold });
        voicePanel.Children.Add(Muted(Texts.T("Нажмите «Записать», сразу пропойте напев и нажмите «Остановить» (запись остановится сама через 6 секунд). Запись заменит встроенный голос для этого символа.")));
        _voiceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 8) };
        voicePanel.Children.Add(_voiceStatus);
        _recordButton = new Button { Padding = new Thickness(12, 7, 12, 7) };
        _recordButton.Click += (_, _) => ToggleRecording();
        _listenButton = new Button { Content = Texts.T("▶ Прослушать"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        _listenButton.Click += (_, _) => Listen();
        _deleteVoiceButton = new Button { Content = Texts.T("Удалить запись"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        _deleteVoiceButton.Click += (_, _) => DeleteRecording();
        var voiceButtons = new WrapPanel();
        voiceButtons.Children.Add(_recordButton);
        voiceButtons.Children.Add(_listenButton);
        voiceButtons.Children.Add(_deleteVoiceButton);
        voicePanel.Children.Add(voiceButtons);
        var voiceCard = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0), Child = voicePanel };
        voiceCard.SetResourceReference(Border.BackgroundProperty, "CardAltBrush");
        panel.Children.Add(voiceCard);
        _recordLimit.Tick += (_, _) => StopRecording();
        Closed += (_, _) =>
        {
            _recordLimit.Stop();
            _recorder.Dispose();
            _player?.Stop();
            _player?.Dispose();
        };
        UpdateVoiceState();

        _saveButton = new Button { Content = Texts.T("Сохранить"), IsDefault = true, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };
        _saveButton.SetResourceReference(StyleProperty, "PrimaryButton");
        _saveButton.Click += (_, _) =>
        {
            Result = ChantBook.Normalize(_chantBox.Text);
            DialogResult = true;
        };
        var builtInButton = new Button { Content = Texts.T("Встроенный"), Padding = new Thickness(12, 8, 12, 8), ToolTip = Texts.T("Вернуть встроенный напев") };
        builtInButton.Click += (_, _) =>
        {
            Result = string.Empty;
            DialogResult = true;
        };
        var cancelButton = new Button { Content = Texts.T("Отмена"), IsCancel = true, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(builtInButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(_saveButton);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) =>
        {
            _chantBox.Focus();
            _chantBox.SelectAll();
        };
        Validate();
    }

    public string Result { get; private set; } = string.Empty;

    public bool VoiceChanged { get; private set; }

    private void ToggleRecording()
    {
        if (_recorder.IsRecording)
        {
            StopRecording();
            return;
        }

        _player?.Stop();
        try
        {
            _recorder.Start();
            _recordLimit.Start();
            _voiceStatus.Text = Texts.T("Идёт запись… Пропойте напев и нажмите «Остановить».");
            _voiceStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            _recordButton.Content = Texts.T("■ Остановить");
            _listenButton.IsEnabled = false;
            _deleteVoiceButton.IsEnabled = false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            _voiceStatus.Text = Texts.F("Не удалось начать запись: {0}. Проверьте, что микрофон подключён и разрешён для приложений в настройках Windows.", exception.Message);
            _voiceStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        }
    }

    private void StopRecording()
    {
        _recordLimit.Stop();
        if (!_recorder.IsRecording)
        {
            return;
        }

        try
        {
            _recorder.StopAndSave(_voicePath);
            VoiceChanged = true;
            UpdateVoiceState();
            Listen();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            UpdateVoiceState();
            _voiceStatus.Text = Texts.F("Не удалось сохранить запись: {0}", exception.Message);
            _voiceStatus.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        }
    }

    /// <summary>Своя запись, если есть, иначе встроенный голос — чтобы сравнить.</summary>
    private void Listen()
    {
        try
        {
            _player?.Stop();
            _player?.Dispose();
            _player = null;
            if (File.Exists(_voicePath))
            {
                _player = new SoundPlayer(_voicePath);
            }
            else if (VoicePackService.Open(_symbol) is { } builtIn)
            {
                _player = new SoundPlayer(builtIn);
            }

            _player?.Play();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            _voiceStatus.Text = Texts.F("Не удалось воспроизвести: {0}", exception.Message);
        }
    }

    private void DeleteRecording()
    {
        try
        {
            _player?.Stop();
            if (File.Exists(_voicePath))
            {
                File.Delete(_voicePath);
                VoiceChanged = true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _voiceStatus.Text = Texts.F("Не удалось удалить запись: {0}", exception.Message);
            return;
        }

        UpdateVoiceState();
    }

    private void UpdateVoiceState()
    {
        var hasRecording = File.Exists(_voicePath);
        _voiceStatus.Text = hasRecording
            ? Texts.F("Своя запись есть — звучит ваш голос ({0}).", Path.GetFileName(_voicePath))
            : Texts.T("Своей записи нет — звучит встроенный голос.");
        _voiceStatus.SetResourceReference(TextBlock.ForegroundProperty, hasRecording ? "PrimaryBrush" : "MutedTextBrush");
        _recordButton.Content = hasRecording ? Texts.T("● Перезаписать") : Texts.T("● Записать");
        _listenButton.Content = hasRecording ? Texts.T("▶ Прослушать") : Texts.T("▶ Встроенный голос");
        _listenButton.IsEnabled = true;
        _deleteVoiceButton.IsEnabled = hasRecording;
    }

    private void Validate()
    {
        var error = ChantBook.Validate(_symbol, _chantBox.Text);
        _statusText.Text = error ?? Texts.T("Напев подходит: слогов столько же, сколько знаков в коде.");
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, error is null ? "PrimaryBrush" : "DangerBrush");
        _saveButton.IsEnabled = error is null;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        return block;
    }
}
