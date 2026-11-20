using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Controls;

/// <summary>
/// Параметры тренировки на вкладке «Тренировка»: что звучит (буквы, цифры, свои символы, особые режимы) и как
/// (группы, скорость, тон, громкость, паузы, сигнал старта, помехи). Каждое изменение сразу сохраняется; сохраняются
/// только эти поля поверх свежих настроек — общие настройки (тема, язык, напоминание) правятся в «Настройках» и не
/// перетираются отсюда.
/// </summary>
public partial class TrainingParamsView : ContentView
{
    private static readonly ContentMode[] MainModes = { ContentMode.Letters, ContentMode.Digits, ContentMode.LettersAndDigits, ContentMode.Custom };
    private readonly HashSet<char> _selectedSymbols = new();
    private MobileSettingsService? _settingsService;
    private bool _ready;

    public TrainingParamsView()
    {
        InitializeComponent();
        // Списки пикеров задаются кодом, чтобы переводиться вместе с интерфейсом
        AlphabetPicker.ItemsSource = new[] { Texts.T("Русский"), Texts.T("Латинский"), Texts.T("Русский и латинский") };
        ContentPicker.ItemsSource = new[]
        {
            Texts.T("Только буквы"), Texts.T("Только цифры"), Texts.T("Буквы и цифры"),
            Texts.T("Все символы"), Texts.T("Выбранные символы"), Texts.T("Метод Коха"),
            Texts.T("Слова"), Texts.T("Позывные"), Texts.T("Q-код и сокращения"), Texts.T("Радиообмен")
        };
        BuildSymbolButtons();
    }

    /// <summary>Подключает хранилище настроек (страница получает его из контейнера) и показывает текущие значения.</summary>
    public void Attach(MobileSettingsService settingsService)
    {
        _settingsService = settingsService;
        Reload();
    }

    /// <summary>Перечитывает настройки: их могли поменять шаг курса, авто-скорость или перенос с другого устройства.</summary>
    public void Reload()
    {
        if (_settingsService is null)
        {
            return;
        }

        _ready = false;
        var settings = _settingsService.LoadSettings();
        AlphabetPicker.SelectedIndex = Math.Clamp(settings.AlphabetIndex, 0, 2);
        ContentPicker.SelectedIndex = (int)ContentModes.Clamp(settings.ContentModeIndex);
        KochStepper.Value = KochMethod.ClampLevel((AlphabetMode)Math.Clamp(settings.AlphabetIndex, 0, 2), settings.KochLevel);
        EmphasizeSwitch.IsToggled = settings.EmphasizeProblemSymbols;
        AutoSpeedSwitch.IsToggled = settings.AutoSpeed;
        GroupCountStepper.Value = Math.Clamp(settings.GroupCount, 1, 100);
        SpeedSlider.Value = Math.Clamp(settings.CharactersPerMinute, 20, 300);
        FrequencySlider.Value = Math.Clamp(settings.FrequencyHz, 300, 1200);
        VolumeSlider.Value = Math.Clamp(settings.VolumePercent, 0, 100);
        CharacterGapSlider.Value = Math.Clamp(settings.CharacterGapUnits, 3, 20);
        GroupGapSlider.Value = Math.Clamp(settings.GroupGapUnits, 7, 30);
        StartSignalSwitch.IsToggled = settings.PlayStartSignal;
        StartPauseSlider.Value = Math.Clamp(settings.StartPauseUnits, 7, 60);
        NoiseSlider.Value = Math.Clamp(settings.NoisePercent, 0, 100);
        QsbSlider.Value = Math.Clamp(settings.QsbPercent, 0, 100);
        DriftSlider.Value = Math.Clamp(settings.DriftHz, 0, NoiseProfile.MaxDriftHz);
        _selectedSymbols.Clear();
        foreach (var symbol in MorseAlphabet.FilterSupportedSymbols(settings.CustomSymbols))
        {
            _selectedSymbols.Add(symbol);
        }

        UpdateLabelsAndSymbols();
        _ready = true;
    }

    // ---------- что тренировать: четыре основных режима ----------

    private void ModeButton_OnClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string value } || !int.TryParse(value, out var mode))
        {
            return;
        }

        // Пикер «Режим (все варианты)» в дополнительных настройках — единственный источник режима
        ContentPicker.SelectedIndex = mode;
    }

    private void UpdateModeButtons()
    {
        var mode = ContentModes.Clamp(ContentPicker.SelectedIndex);
        ChoiceButtons.Highlight(new[] { ModeLettersButton, ModeDigitsButton, ModeBothButton, ModeCustomButton }, Array.IndexOf(MainModes, mode));

        CustomSymbolsLayout.IsVisible = mode == ContentMode.Custom;
        var alphabet = (AlphabetMode)Math.Clamp(AlphabetPicker.SelectedIndex, 0, 2);
        var alphabetName = AlphabetPicker.SelectedItem as string ?? string.Empty;
        ModeHintLabel.Text = Array.IndexOf(MainModes, mode) < 0
            ? Texts.F("Сейчас особый режим «{0}» — он выбран в дополнительных настройках.", ContentPicker.SelectedItem as string ?? string.Empty)
            : Texts.F("Символов в задании: {0} · алфавит: {1} (меняется в дополнительных настройках)",
                MorseAlphabet.BuildPool(alphabet, mode, new string(_selectedSymbols.ToArray()), (int)KochStepper.Value).Count, alphabetName);
    }

    private void AdvancedButton_OnClicked(object sender, EventArgs e)
    {
        AdvancedLayout.IsVisible = !AdvancedLayout.IsVisible;
        AdvancedButton.Text = AdvancedLayout.IsVisible ? Texts.T("Дополнительные настройки ▴") : Texts.T("Дополнительные настройки ▾");
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

    /// <summary>Записывает параметры тренировки поверх свежих настроек: тема, язык и напоминание не трогаются.</summary>
    private void SaveControlsToSettings()
    {
        UpdateLabelsAndSymbols();
        if (_settingsService is null || !_ready)
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        settings.AlphabetIndex = Math.Clamp(AlphabetPicker.SelectedIndex, 0, 2);
        settings.ContentModeIndex = (int)ContentModes.Clamp(ContentPicker.SelectedIndex);
        settings.KochLevel = (int)KochStepper.Value;
        settings.EmphasizeProblemSymbols = EmphasizeSwitch.IsToggled;
        settings.AutoSpeed = AutoSpeedSwitch.IsToggled;
        settings.GroupCount = (int)Math.Round(GroupCountStepper.Value);
        settings.CharactersPerMinute = Snap(SpeedSlider.Value, 5, 20, 300);
        settings.FrequencyHz = Snap(FrequencySlider.Value, 50, 300, 1200);
        settings.VolumePercent = Snap(VolumeSlider.Value, 5, 0, 100);
        settings.CharacterGapUnits = (int)Math.Round(CharacterGapSlider.Value);
        settings.GroupGapUnits = (int)Math.Round(GroupGapSlider.Value);
        settings.PlayStartSignal = StartSignalSwitch.IsToggled;
        settings.StartPauseUnits = (int)Math.Round(StartPauseSlider.Value);
        settings.NoisePercent = Snap(NoiseSlider.Value, 5, 0, 100);
        settings.QsbPercent = Snap(QsbSlider.Value, 5, 0, 100);
        settings.DriftHz = Snap(DriftSlider.Value, 5, 0, NoiseProfile.MaxDriftHz);
        settings.CustomSymbols = new string(_selectedSymbols.ToArray());
        _settingsService.SaveSettings(settings);
        SaveStatusLabel.Text = Texts.F("Сохранено {0:HH:mm} · действует со следующего прослушивания", DateTime.Now);
    }

    private void UpdateLabelsAndSymbols()
    {
        GroupCountValue.Text = ((int)Math.Round(GroupCountStepper.Value)).ToString();
        SpeedValue.Text = Texts.F("{0} зн/мин", Snap(SpeedSlider.Value, 5, 20, 300));
        FrequencyValue.Text = Texts.F("{0} Гц", Snap(FrequencySlider.Value, 50, 300, 1200));
        VolumeValue.Text = $"{Snap(VolumeSlider.Value, 5, 0, 100)}%";
        CharacterGapValue.Text = Texts.F("{0} точек", (int)Math.Round(CharacterGapSlider.Value));
        GroupGapValue.Text = Texts.F("{0} точек", (int)Math.Round(GroupGapSlider.Value));
        StartPauseValue.Text = Texts.F("{0} точек", (int)Math.Round(StartPauseSlider.Value));
        NoiseValue.Text = $"{Snap(NoiseSlider.Value, 5, 0, 100)}%";
        QsbValue.Text = $"{Snap(QsbSlider.Value, 5, 0, 100)}%";
        DriftValue.Text = Texts.F("±{0} Гц", Snap(DriftSlider.Value, 5, 0, NoiseProfile.MaxDriftHz));
        SelectedSymbolsLabel.Text = Texts.F("Выбрано: {0}", _selectedSymbols.Count);
        UpdateModeButtons();
        var kochAlphabet = (AlphabetMode)Math.Clamp(AlphabetPicker.SelectedIndex, 0, 2);
        KochLayout.IsVisible = ContentPicker.SelectedIndex == (int)ContentMode.Koch;
        KochStepper.Maximum = KochMethod.MaxLevel(kochAlphabet);
        var kochLevel = KochMethod.ClampLevel(kochAlphabet, (int)KochStepper.Value);
        KochLabel.Text = Texts.F("Метод Коха: уровень {0} из {1}", kochLevel, KochMethod.MaxLevel(kochAlphabet));
        KochSummaryLabel.Text = KochMethod.Describe(kochAlphabet, kochLevel);
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

    private void FarnsworthButton_OnClicked(object sender, EventArgs e)
    {
        var preset = new AppSettings { CharactersPerMinute = Snap(SpeedSlider.Value, 5, 20, 300) };
        TrainingPresets.ApplyFarnsworth(preset);
        _ready = false;
        SpeedSlider.Value = preset.CharactersPerMinute;
        CharacterGapSlider.Value = preset.CharacterGapUnits;
        GroupGapSlider.Value = preset.GroupGapUnits;
        _ready = true;
        SaveControlsToSettings();
        SaveStatusLabel.Text = Texts.T("Пресет Фарнсворта применён");
    }

    private static int Snap(double value, int step, int minimum, int maximum)
    {
        return Math.Clamp((int)Math.Round(value / step) * step, minimum, maximum);
    }
}
