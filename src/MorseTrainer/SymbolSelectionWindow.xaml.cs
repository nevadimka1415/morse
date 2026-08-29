using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MorseTrainer.Domain;

namespace MorseTrainer;

public partial class SymbolSelectionWindow : Window
{
    private readonly List<ToggleButton> _symbolButtons = new();

    public SymbolSelectionWindow(string initialSelection)
    {
        InitializeComponent();
        AddSymbols(RussianSymbolsPanel, MorseAlphabet.Russian.Keys, initialSelection);
        AddSymbols(LatinSymbolsPanel, MorseAlphabet.Latin.Keys, initialSelection);
        AddSymbols(DigitSymbolsPanel, MorseAlphabet.Digits.Keys, initialSelection);
        AddSymbols(PunctuationSymbolsPanel, MorseAlphabet.Punctuation.Keys, initialSelection);
        UpdateSelectionCount();
    }

    public string SelectedSymbols { get; private set; } = string.Empty;

    private void AddSymbols(Panel panel, IEnumerable<char> symbols, string initialSelection)
    {
        foreach (var symbol in symbols)
        {
            var button = new ToggleButton
            {
                Content = symbol.ToString(),
                Tag = symbol,
                IsChecked = initialSelection.Contains(symbol),
                Style = (Style)FindResource("SymbolToggleStyle")
            };
            button.Checked += SymbolButton_OnChanged;
            button.Unchecked += SymbolButton_OnChanged;
            panel.Children.Add(button);
            _symbolButtons.Add(button);
        }
    }

    private void SymbolButton_OnChanged(object sender, RoutedEventArgs e)
    {
        UpdateSelectionCount();
    }

    private void SelectCategory_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category })
        {
            return;
        }

        var panel = category switch
        {
            "Russian" => RussianSymbolsPanel,
            "Latin" => LatinSymbolsPanel,
            "Digits" => DigitSymbolsPanel,
            _ => PunctuationSymbolsPanel
        };
        foreach (var button in panel.Children.OfType<ToggleButton>())
        {
            button.IsChecked = true;
        }
    }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var button in _symbolButtons)
        {
            button.IsChecked = true;
        }
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var button in _symbolButtons)
        {
            button.IsChecked = false;
        }
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = _symbolButtons
            .Where(button => button.IsChecked == true)
            .Select(button => (char)button.Tag)
            .ToArray();
        if (selected.Length == 0)
        {
            MessageBox.Show("Выберите хотя бы один символ.", "Morse Trainer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedSymbols = new string(selected);
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateSelectionCount()
    {
        if (SelectionCountText is not null)
        {
            SelectionCountText.Text = $"Выбрано: {_symbolButtons.Count(button => button.IsChecked == true)}";
        }
    }
}
