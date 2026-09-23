using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MorseTrainer.Domain;
using MorseTrainer.Localization;

namespace MorseTrainer;

/// <summary>
/// Окно «Изменить напев»: свой напев для символа с проверкой числа слогов на лету.
/// Result — новый напев или пустая строка, если нажато «Встроенный» (вернуть встроенный).
/// </summary>
public sealed class ChantEditorWindow : Window
{
    private readonly char _symbol;
    private readonly TextBox _chantBox;
    private readonly TextBlock _statusText;
    private readonly Button _saveButton;

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
