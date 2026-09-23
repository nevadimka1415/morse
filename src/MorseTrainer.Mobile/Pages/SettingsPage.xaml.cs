using System.Net.Http;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Devices;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Models;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private static readonly HttpClient UpdateClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly MobileSettingsService _settingsService;
    private readonly IReminderService _reminders;
    private AppSettings _settings = new();
    private IReadOnlyList<TrainingProfile> _profiles = Array.Empty<TrainingProfile>();
    private readonly HashSet<char> _selectedSymbols = new();
    private bool _ready;

    public SettingsPage(MobileSettingsService settingsService, IReminderService reminders)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _reminders = reminders;
        // Списки пикеров задаются кодом, чтобы переводиться вместе с интерфейсом
        ThemePicker.ItemsSource = new[] { Texts.T("Системная"), Texts.T("Тёмная"), Texts.T("Светлая") };
        LanguagePicker.ItemsSource = new[] { Texts.T("Системный"), Texts.T("Русский (Russian)"), Texts.T("English") };
        AlphabetPicker.ItemsSource = new[] { Texts.T("Русский"), Texts.T("Латинский"), Texts.T("Русский и латинский") };
        ContentPicker.ItemsSource = new[]
        {
            Texts.T("Только буквы"), Texts.T("Только цифры"), Texts.T("Буквы и цифры"),
            Texts.T("Все символы"), Texts.T("Выбранные символы"), Texts.T("Метод Коха"),
            Texts.T("Слова"), Texts.T("Позывные"), Texts.T("Q-код и сокращения")
        };
        ExamPlaybacksPicker.ItemsSource = Enumerable.Range(ExamSession.MinPlaybacks, ExamSession.MaxAllowedPlaybacks)
            .Select(count => count.ToString()).ToList();
        ExamLimitPicker.ItemsSource = ExamSession.TimeLimitChoices
            .Select(minutes => minutes == 0 ? Texts.T("без лимита") : Texts.F("{0} мин", minutes)).ToList();
        VersionLabel.Text = $"Morse Trainer {CurrentVersion}";
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

        RefreshCrashReport();
    }

    // ---------- напоминание ----------

    private void UpdateReminderStatus()
    {
        ReminderStatusLabel.Text = _settings.ReminderEnabled
            ? Texts.F("Каждый день в {0}", ReminderSchedule.ToTime(_settings.ReminderMinutes).ToString(@"hh\:mm"))
            : Texts.T("Напоминание выключено");
    }

    private async void ReminderSwitch_OnToggled(object sender, ToggledEventArgs e)
    {
        if (_ready)
        {
            await ApplyReminderAsync();
        }
    }

    private async void ReminderTimePicker_OnPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_ready && e.PropertyName == TimePicker.TimeProperty.PropertyName)
        {
            await ApplyReminderAsync();
        }
    }

    /// <summary>Сохраняет переключатель и время; при включении просит разрешение и ставит уведомление.</summary>
    private async Task ApplyReminderAsync()
    {
        _settings.ReminderMinutes = ReminderSchedule.ToMinutes(ReminderTimePicker.Time ?? ReminderSchedule.ToTime(ReminderSchedule.DefaultMinutes));
        _settings.ReminderEnabled = ReminderSwitch.IsToggled;
        try
        {
            if (_settings.ReminderEnabled)
            {
                if (!await _reminders.RequestPermissionAsync())
                {
                    _settings.ReminderEnabled = false;
                    _ready = false;
                    ReminderSwitch.IsToggled = false;
                    _ready = true;
                    await DisplayAlertAsync(Texts.T("Напоминание"),
                        Texts.T("Уведомления запрещены: разрешите их для Morse Trainer в настройках телефона."), Texts.T("Понятно"));
                }
                else
                {
                    await _reminders.ScheduleDailyAsync(ReminderSchedule.ToTime(_settings.ReminderMinutes), ReminderTexts.Title, ReminderTexts.Body(_settings));
                }
            }
            else
            {
                _reminders.Cancel();
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Напоминание"), exception.Message, Texts.T("Закрыть"));
        }

        _settingsService.SaveSettings(_settings);
        UpdateReminderStatus();
    }

    // ---------- отчёт о сбое ----------

    private void RefreshCrashReport()
    {
        var lastCrash = CrashReport.LastCrashAt(MobilePaths.CrashLogFile);
        CrashReportLayout.IsVisible = lastCrash is not null;
        if (lastCrash is not null)
        {
            CrashReportLabel.Text = Texts.F("Отчёт о сбое от {0:dd.MM.yyyy HH:mm}", lastCrash.Value);
        }
    }

    private async void ShareCrashReportButton_OnClicked(object sender, EventArgs e)
    {
        var path = MobilePaths.CrashLogFile;
        if (!CrashReport.Exists(path))
        {
            RefreshCrashReport();
            return;
        }

        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = Texts.T("Отчёт о сбое Morse Trainer"),
                File = new ShareFile(path)
            });
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private void DeleteCrashReportButton_OnClicked(object sender, EventArgs e)
    {
        CrashReport.Delete(MobilePaths.CrashLogFile);
        RefreshCrashReport();
        SaveStatusLabel.Text = Texts.T("Отчёт о сбое удалён");
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
        LanguagePicker.SelectedIndex = Math.Clamp(_settings.LanguageIndex, 0, 2);
        AlphabetPicker.SelectedIndex = Math.Clamp(_settings.AlphabetIndex, 0, 2);
        ContentPicker.SelectedIndex = (int)ContentModes.Clamp(_settings.ContentModeIndex);
        KochStepper.Value = KochMethod.ClampLevel((AlphabetMode)Math.Clamp(_settings.AlphabetIndex, 0, 2), _settings.KochLevel);
        EmphasizeSwitch.IsToggled = _settings.EmphasizeProblemSymbols;
        AutoSpeedSwitch.IsToggled = _settings.AutoSpeed;
        ExamPlaybacksPicker.SelectedIndex = ExamSession.ClampPlaybacks(_settings.ExamPlaybacks) - 1;
        ExamLimitPicker.SelectedIndex = Math.Max(0, ExamSession.TimeLimitChoices.ToList().IndexOf(ExamSession.ClampTimeLimit(_settings.ExamTimeLimitMinutes)));
        GroupCountStepper.Value = Math.Clamp(_settings.GroupCount, 1, 100);
        SpeedSlider.Value = Math.Clamp(_settings.CharactersPerMinute, 20, 300);
        FrequencySlider.Value = Math.Clamp(_settings.FrequencyHz, 300, 1200);
        VolumeSlider.Value = Math.Clamp(_settings.VolumePercent, 0, 100);
        CharacterGapSlider.Value = Math.Clamp(_settings.CharacterGapUnits, 3, 20);
        GroupGapSlider.Value = Math.Clamp(_settings.GroupGapUnits, 7, 30);
        StartSignalSwitch.IsToggled = _settings.PlayStartSignal;
        StartPauseSlider.Value = Math.Clamp(_settings.StartPauseUnits, 7, 60);
        ReminderSwitch.IsToggled = _settings.ReminderEnabled;
        ReminderTimePicker.Time = ReminderSchedule.ToTime(_settings.ReminderMinutes);
        UpdateReminderStatus();
        NoiseSlider.Value = Math.Clamp(_settings.NoisePercent, 0, 100);
        QsbSlider.Value = Math.Clamp(_settings.QsbPercent, 0, 100);
        DriftSlider.Value = Math.Clamp(_settings.DriftHz, 0, NoiseProfile.MaxDriftHz);
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

    private async void LanguagePicker_OnChanged(object sender, EventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        SaveControlsToSettings();
        await DisplayAlertAsync("Morse Trainer", Texts.T("Язык интерфейса изменится после перезапуска приложения."), Texts.T("Понятно"));
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
        _settings.LanguageIndex = Math.Clamp(LanguagePicker.SelectedIndex, 0, 2);
        _settings.AlphabetIndex = Math.Clamp(AlphabetPicker.SelectedIndex, 0, 2);
        _settings.ContentModeIndex = (int)ContentModes.Clamp(ContentPicker.SelectedIndex);
        _settings.KochLevel = (int)KochStepper.Value;
        _settings.EmphasizeProblemSymbols = EmphasizeSwitch.IsToggled;
        _settings.AutoSpeed = AutoSpeedSwitch.IsToggled;
        _settings.ExamPlaybacks = ExamSession.ClampPlaybacks(ExamPlaybacksPicker.SelectedIndex + 1);
        _settings.ExamTimeLimitMinutes = ExamSession.TimeLimitChoices[Math.Clamp(ExamLimitPicker.SelectedIndex, 0, ExamSession.TimeLimitChoices.Count - 1)];
        _settings.GroupCount = (int)Math.Round(GroupCountStepper.Value);
        _settings.CharactersPerMinute = Snap(SpeedSlider.Value, 5, 20, 300);
        _settings.FrequencyHz = Snap(FrequencySlider.Value, 50, 300, 1200);
        _settings.VolumePercent = Snap(VolumeSlider.Value, 5, 0, 100);
        _settings.CharacterGapUnits = (int)Math.Round(CharacterGapSlider.Value);
        _settings.GroupGapUnits = (int)Math.Round(GroupGapSlider.Value);
        _settings.PlayStartSignal = StartSignalSwitch.IsToggled;
        _settings.StartPauseUnits = (int)Math.Round(StartPauseSlider.Value);
        _settings.NoisePercent = Snap(NoiseSlider.Value, 5, 0, 100);
        _settings.QsbPercent = Snap(QsbSlider.Value, 5, 0, 100);
        _settings.DriftHz = Snap(DriftSlider.Value, 5, 0, NoiseProfile.MaxDriftHz);
        _settings.CustomSymbols = new string(_selectedSymbols.ToArray());
        ApplyTheme(_settings.ThemeIndex);
        UpdateLabelsAndSymbols();
        _settingsService.SaveSettings(_settings);
        SaveStatusLabel.Text = Texts.F("Сохранено {0:HH:mm}", DateTime.Now);
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
            SaveStatusLabel.Text = Texts.F("Профиль «{0}» сохранён", name);
        }
        catch (ArgumentException)
        {
            await DisplayAlertAsync(Texts.T("Название профиля"), Texts.T("Введите название профиля."), Texts.T("Понятно"));
        }
    }

    private async void ShareProfilesButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            var text = ProfileTransfer.Export(_profiles);
            await Clipboard.Default.SetTextAsync(text);
            await Share.Default.RequestAsync(new ShareTextRequest { Title = Texts.T("Профили Morse Trainer"), Text = text });
            SaveStatusLabel.Text = Texts.T("Профили скопированы в буфер и отправлены");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private async void ImportProfilesButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            var text = await Clipboard.Default.GetTextAsync();
            var imported = ProfileTransfer.Import(text ?? string.Empty);
            _profiles = _settingsService.ImportProfiles(imported);
            LoadAll();
            SaveStatusLabel.Text = Texts.F("Импортировано профилей: {0}", imported.Count);
        }
        catch (FormatException exception)
        {
            await DisplayAlertAsync(Texts.T("Импорт профилей"),
                Texts.F("{0}\nСкопируйте текст профилей (из «Поделиться» на другом устройстве) и нажмите кнопку снова.", exception.Message), Texts.T("Понятно"));
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

    private static Version CurrentVersion =>
        UpdateService.ParseVersion(AppInfo.Current.VersionString) ?? new Version(0, 0, 0);

    // Единственный выход в интернет, и только по нажатию кнопки
    private async void CheckUpdatesButton_OnClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        if (button is not null)
        {
            button.IsEnabled = false;
        }

        try
        {
            var info = await UpdateService.FetchLatestAsync(UpdateClient, CancellationToken.None);
            if (UpdateService.IsNewer(CurrentVersion, info.LatestVersion))
            {
                var notes = string.IsNullOrWhiteSpace(info.Notes) ? string.Empty : "\n\n" + info.Notes;
                var download = await DisplayAlertAsync(Texts.T("Есть обновление"),
                    Texts.F("Доступна версия {0}, у вас {1}.{2}", info.LatestVersion, CurrentVersion, notes), Texts.T("Скачать"), Texts.T("Позже"));
                if (download)
                {
                    var url = DeviceInfo.Current.Platform == DevicePlatform.Android
                        ? info.AndroidApkUrl ?? info.ReleasePageUrl
                        : info.ReleasePageUrl;
                    await Launcher.Default.OpenAsync(new Uri(url));
                }
            }
            else
            {
                await DisplayAlertAsync(Texts.T("Обновлений нет"), Texts.F("У вас последняя версия {0}.", CurrentVersion), Texts.T("Понятно"));
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось проверить"),
                Texts.F("Проверьте подключение к интернету.\n\n{0}", exception.Message), Texts.T("Закрыть"));
        }
        finally
        {
            if (button is not null)
            {
                button.IsEnabled = true;
            }
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

    // Планшет или альбомная ориентация: центрируем контент полосой до 720 px
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
        {
            RootLayout.Padding = TabletLayout.PaddingFor(width);
        }
    }
}
