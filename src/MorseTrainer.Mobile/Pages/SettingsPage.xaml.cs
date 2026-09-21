using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly MobileSettingsService _settingsService;
    private AppSettings _settings = new();
    private IReadOnlyList<TrainingProfile> _profiles = Array.Empty<TrainingProfile>();
    private readonly HashSet<char> _selectedSymbols = new();
    private bool _ready;

    public SettingsPage(MobileSettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        BuildSymbolButtons();
        LoadAll();
        _ready = true;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_ready)
        {
            LoadAll();
        }
    }

    private void LoadAll()
    {
        _ready = false;
        _settings = _settingsService.LoadSettings();
        _profiles = _settingsService.LoadProfiles();
        // Picker.ItemsSource требует IList, а сервис отдаёт IReadOnlyList — копируем в список
        ProfilePicker.ItemsSource = _profiles.ToList();
        ProfilePicker.SelectedItem = _profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase)) ?? _profiles[0];
        ProfileNameEntry.Text = ((TrainingProfile)ProfilePicker.SelectedItem).Name;
        ApplySettingsToControls();
        _ready = true;
    }

    private void ApplySettingsToControls()
    {
        ThemePicker.SelectedIndex = Math.Clamp(_settings.ThemeIndex, 0, 2);
        AlphabetPicker.SelectedIndex = Math.Clamp(_settings.AlphabetIndex, 0, 2);
        ContentPicker.SelectedIndex = Math.Clamp(_settings.ContentModeIndex, 0, 4);
        GroupCountStepper.Value = Math.Clamp(_settings.GroupCount, 1, 100);
        SpeedSlider.Value = Math.Clamp(_settings.CharactersPerMinute, 20, 300);
        FrequencySlider.Value = Math.Clamp(_settings.FrequencyHz, 300, 1200);
        VolumeSlider.Value = Math.Clamp(_settings.VolumePercent, 0, 100);
        CharacterGapSlider.Value = Math.Clamp(_settings.CharacterGapUnits, 3, 20);
        GroupGapSlider.Value = Math.Clamp(_settings.GroupGapUnits, 7, 30);
        StartSignalSwitch.IsToggled = _settings.PlayStartSignal;
        StartPauseSlider.Value = Math.Clamp(_settings.StartPauseUnits, 7, 60);
        _selectedSymbols.Clear();
        foreach (var symbol in MorseAlphabet.FilterSupportedSymbols(_settings.CustomSymbols))
        {
            _selectedSymbols.Add(symbol);
        }

        ApplyTheme(_settings.ThemeIndex);
        UpdateLabelsAndSymbols();
    }

    private void BuildSymbolButtons()
    {
        var symbols = MorseAlphabet.Russian.Keys
            .Concat(MorseAlphabet.Latin.Keys)
            .Concat(MorseAlphabet.Digits.Keys)
            .Distinct()
            .ToArray();
        foreach (var symbol in symbols)
        {
            var button = new Button
            {
                Text = symbol.ToString(),
                CommandParameter = symbol,
                WidthRequest = 48,
                HeightRequest = 44,
                Padding = 4,
                Margin = 3
            };
            button.Clicked += SymbolButton_OnClicked;
            SymbolsFlex.Children.Add(button);
        }
    }

    private void SymbolButton_OnClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: char symbol })
        {
            return;
        }

        if (!_selectedSymbols.Add(symbol))
        {
            _selectedSymbols.Remove(symbol);
        }

        ContentPicker.SelectedIndex = (int)ContentMode.Custom;
        SaveControlsToSettings();
    }

    private void SelectAllButton_OnClicked(object sender, EventArgs e)
    {
        _selectedSymbols.Clear();
        foreach (var button in SymbolsFlex.Children.OfType<Button>())
        {
            if (button.CommandParameter is char symbol)
            {
                _selectedSymbols.Add(symbol);
            }
        }

        ContentPicker.SelectedIndex = (int)ContentMode.Custom;
        SaveControlsToSettings();
    }

    private void ClearSymbolsButton_OnClicked(object sender, EventArgs e)
    {
        _selectedSymbols.Clear();
        ContentPicker.SelectedIndex = (int)ContentMode.Custom;
        SaveControlsToSettings();
    }

    private void SettingsControl_OnChanged(object sender, EventArgs e)
    {
        if (_ready)
        {
            SaveControlsToSettings();
        }
    }

    private void SaveControlsToSettings()
    {
        _settings.ThemeIndex = Math.Clamp(ThemePicker.SelectedIndex, 0, 2);
        _settings.AlphabetIndex = Math.Clamp(AlphabetPicker.SelectedIndex, 0, 2);
        _settings.ContentModeIndex = Math.Clamp(ContentPicker.SelectedIndex, 0, 4);
        _settings.GroupCount = (int)Math.Round(GroupCountStepper.Value);
        _settings.CharactersPerMinute = Snap(SpeedSlider.Value, 10, 20, 300);
        _settings.FrequencyHz = Snap(FrequencySlider.Value, 50, 300, 1200);
        _settings.VolumePercent = Snap(VolumeSlider.Value, 5, 0, 100);
        _settings.CharacterGapUnits = (int)Math.Round(CharacterGapSlider.Value);
        _settings.GroupGapUnits = (int)Math.Round(GroupGapSlider.Value);
        _settings.PlayStartSignal = StartSignalSwitch.IsToggled;
        _settings.StartPauseUnits = (int)Math.Round(StartPauseSlider.Value);
        _settings.CustomSymbols = new string(_selectedSymbols.ToArray());
        ApplyTheme(_settings.ThemeIndex);
        UpdateLabelsAndSymbols();
        _settingsService.SaveSettings(_settings);
        SaveStatusLabel.Text = $"Сохранено {DateTime.Now:HH:mm}";
    }

    private void UpdateLabelsAndSymbols()
    {
        GroupCountValue.Text = ((int)Math.Round(GroupCountStepper.Value)).ToString();
        SpeedValue.Text = $"{Snap(SpeedSlider.Value, 10, 20, 300)} зн/мин";
        FrequencyValue.Text = $"{Snap(FrequencySlider.Value, 50, 300, 1200)} Гц";
        VolumeValue.Text = $"{Snap(VolumeSlider.Value, 5, 0, 100)}%";
        CharacterGapValue.Text = $"{(int)Math.Round(CharacterGapSlider.Value)} точек";
        GroupGapValue.Text = $"{(int)Math.Round(GroupGapSlider.Value)} точек";
        StartPauseValue.Text = $"{(int)Math.Round(StartPauseSlider.Value)} точек";
        SelectedSymbolsLabel.Text = $"Выбрано: {_selectedSymbols.Count}";
        foreach (var button in SymbolsFlex.Children.OfType<Button>())
        {
            var selected = button.CommandParameter is char symbol && _selectedSymbols.Contains(symbol);
            button.BackgroundColor = selected
                ? (Color)Application.Current!.Resources["Primary"]
                : Colors.Transparent;
            button.TextColor = selected
                ? Color.FromArgb("#06231C")
                : Application.Current!.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black;
        }
    }

    private void ProfilePicker_OnChanged(object sender, EventArgs e)
    {
        if (!_ready || ProfilePicker.SelectedItem is not TrainingProfile profile)
        {
            return;
        }

        profile.ApplyTo(_settings);
        ProfileNameEntry.Text = profile.Name;
        _ready = false;
        ApplySettingsToControls();
        _ready = true;
        _settingsService.SaveSettings(_settings);
    }

    private async void SaveProfileButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            SaveControlsToSettings();
            var name = TrainingProfile.NormalizeName(ProfileNameEntry.Text);
            _profiles = _settingsService.SaveProfile(name, _settings);
            LoadAll();
            SaveStatusLabel.Text = $"Профиль «{name}» сохранён";
        }
        catch (ArgumentException)
        {
            await DisplayAlertAsync("Название профиля", "Введите название профиля.", "Понятно");
        }
    }

    private void DeleteProfileButton_OnClicked(object sender, EventArgs e)
    {
        if (ProfilePicker.SelectedItem is not TrainingProfile profile)
        {
            return;
        }

        _profiles = _settingsService.DeleteProfile(profile.Name);
        _settings.ActiveProfileName = _profiles[0].Name;
        _profiles[0].ApplyTo(_settings);
        _settingsService.SaveSettings(_settings);
        LoadAll();
    }

    private static int Snap(double value, int step, int minimum, int maximum)
    {
        return Math.Clamp((int)Math.Round(value / step) * step, minimum, maximum);
    }

    private static void ApplyTheme(int themeIndex)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.UserAppTheme = themeIndex switch
        {
            1 => AppTheme.Dark,
            2 => AppTheme.Light,
            _ => AppTheme.Unspecified
        };
    }
}
