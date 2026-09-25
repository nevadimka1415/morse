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
    private readonly ChantStore _chantStore;
    private readonly IAudioPlaybackService _audioPlayback;
    private AppSettings _settings = new();
    private readonly HashSet<char> _selectedSymbols = new();
    private bool _ready;

    public SettingsPage(MobileSettingsService settingsService, IReminderService reminders, ChantStore chantStore, IAudioPlaybackService audioPlayback)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _reminders = reminders;
        _chantStore = chantStore;
        _audioPlayback = audioPlayback;
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

    // Одно устройство — один человек: профилей на телефоне нет, настройки одни
    private void LoadAll()
    {
        _ready = false;
        _settings = _settingsService.LoadSettings();
        ApplySettingsToControls();
        _ready = true;
    }

    // ---------- что тренировать: четыре основных режима ----------

    private static readonly ContentMode[] MainModes = { ContentMode.Letters, ContentMode.Digits, ContentMode.LettersAndDigits, ContentMode.Custom };

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

    private async void ShareSettingsButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            // Настройки уходят одним профилем, вместе со своими напевами
            SaveControlsToSettings();
            var text = ProfileTransfer.Export(new[] { TrainingProfile.FromSettings(_settings.ActiveProfileName, _settings) }, LearningCatalog.CustomChants);
            await Clipboard.Default.SetTextAsync(text);
            await Share.Default.RequestAsync(new ShareTextRequest { Title = Texts.T("Настройки Morse Trainer"), Text = text });
            SaveStatusLabel.Text = Texts.T("Настройки скопированы в буфер и отправлены");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private async void ImportSettingsButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            var text = await Clipboard.Default.GetTextAsync();
            var package = ProfileTransfer.ImportPackage(text ?? string.Empty);
            // Профилей на телефоне нет: берём одноимённый профиль из текста, иначе первый, и ставим его настройки
            var profile = package.Profiles.FirstOrDefault(item => string.Equals(item.Name, _settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
                          ?? package.Profiles[0];
            var name = _settings.ActiveProfileName;
            profile.ApplyTo(_settings);
            _settings.ActiveProfileName = name;
            _settingsService.SaveSettings(_settings);
            LoadAll();
            SaveStatusLabel.Text = Texts.T("Настройки приняты");
            if (package.Chants.Count > 0)
            {
                LearningCatalog.SetCustomChants(_chantStore.Merge(package.Chants));
                SaveStatusLabel.Text += " · " + Texts.F("Своих напевов: {0}.", package.Chants.Count);
            }
        }
        catch (FormatException exception)
        {
            await DisplayAlertAsync(Texts.T("Перенос настроек"),
                Texts.F("{0}\nСкопируйте текст настроек (из «Поделиться» на другом устройстве) и нажмите кнопку снова.", exception.Message), Texts.T("Понятно"));
        }
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

    // Пасхалка: нажатие на строку версии — «НЕВАДИМКА» азбукой на 200 знаков/мин
    private async void VersionLabel_OnTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            var clip = MorseAudioService.Render(EasterEgg.Text, EasterEgg.Speed, settings.FrequencyHz, settings.VolumePercent, 3, 7);
            var path = await AudioFileService.SaveClipAsync(clip, "easter-egg.wav");
            _audioPlayback.Stop();
            await _audioPlayback.PlayAsync(path);
        }
        catch (Exception)
        {
            // Нет звука или другой звук прервал этот — пасхалка просто молчит
        }
    }
}
