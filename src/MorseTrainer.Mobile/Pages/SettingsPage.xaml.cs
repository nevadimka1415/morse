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
    private readonly TrainingHistoryStore _historyStore;
    private bool _ready;

    public SettingsPage(MobileSettingsService settingsService, IReminderService reminders, ChantStore chantStore, IAudioPlaybackService audioPlayback,
        TrainingHistoryStore historyStore)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _reminders = reminders;
        _chantStore = chantStore;
        _audioPlayback = audioPlayback;
        _historyStore = historyStore;
        // Списки пикеров задаются кодом, чтобы переводиться вместе с интерфейсом
        ThemePicker.ItemsSource = new[] { Texts.T("Системная"), Texts.T("Тёмная"), Texts.T("Светлая") };
        LanguagePicker.ItemsSource = new[] { Texts.T("Системный"), Texts.T("Русский (Russian)"), Texts.T("English") };
        VersionLabel.Text = $"Morse Trainer {CurrentVersion}";
#if RUSTORE
        // Сборка для RuStore (-p:DistributionChannel=RuStore): обновления ставит сам магазин — по правилам RuStore
        // приложение из него должно обновляться только через RuStore, поэтому своей проверки через GitHub нет
        CheckUpdatesButton.IsVisible = false;
        UpdatesNoteLabel.Text = Texts.T("Обновления приходят через RuStore. Интернет нужен только для отправки сообщения разработчику.");
#endif
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

    private void UpdateReminderStatus(AppSettings settings)
    {
        ReminderStatusLabel.Text = settings.ReminderEnabled
            ? Texts.F("Каждый день в {0}", ReminderSchedule.ToTime(settings.ReminderMinutes).ToString(@"hh\:mm"))
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
        // Свежие настройки: параметры тренировки с вкладки «Тренировка» не перетираются
        var settings = _settingsService.LoadSettings();
        settings.ReminderMinutes = ReminderSchedule.ToMinutes(ReminderTimePicker.Time ?? ReminderSchedule.ToTime(ReminderSchedule.DefaultMinutes));
        settings.ReminderEnabled = ReminderSwitch.IsToggled;
        try
        {
            if (settings.ReminderEnabled)
            {
                if (!await _reminders.RequestPermissionAsync())
                {
                    settings.ReminderEnabled = false;
                    _ready = false;
                    ReminderSwitch.IsToggled = false;
                    _ready = true;
                    await DisplayAlertAsync(Texts.T("Напоминание"),
                        Texts.T("Уведомления запрещены: разрешите их для Morse Trainer в настройках телефона."), Texts.T("Понятно"));
                }
                else
                {
                    await _reminders.ScheduleDailyAsync(ReminderSchedule.ToTime(settings.ReminderMinutes), ReminderTexts.Title, ReminderTexts.Body(settings, _historyStore.Load()));
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

        // Пока ждали разрешения, могли поменять что-то ещё — сохраняются только поля напоминания
        var latest = _settingsService.LoadSettings();
        latest.ReminderEnabled = settings.ReminderEnabled;
        latest.ReminderMinutes = settings.ReminderMinutes;
        _settingsService.SaveSettings(latest);
        UpdateReminderStatus(latest);
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

    // Перенос прогресса (история занятий = отметки курса) на новый телефон — файлом, как «Экспорт/Импорт истории» на Windows
    private async void SaveProgressButton_OnClicked(object sender, EventArgs e)
    {
        var history = _historyStore.Load();
        if (history.Count == 0)
        {
            await DisplayAlertAsync(Texts.T("Прогресс"), Texts.T("Пока нечего сохранять: занятий ещё не было."), Texts.T("Понятно"));
            return;
        }

        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"morse-progress-{DateTime.Now:yyyy-MM-dd}.json");
            await File.WriteAllTextAsync(path, HistoryTransfer.Export(history));
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = Texts.T("Прогресс Morse Trainer"),
                File = new ShareFile(path, "application/json")
            });
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private async void LoadProgressButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Texts.T("Загрузить прогресс…") });
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var result = _historyStore.Merge(HistoryTransfer.Import(await reader.ReadToEndAsync()));
            await DisplayAlertAsync(Texts.T("Прогресс"), result.Describe(), Texts.T("Понятно"));
        }
        catch (Exception exception)
        {
            // Не тот файл или нет доступа — сообщение, а не падение из async void
            await DisplayAlertAsync(Texts.T("Прогресс"), exception.Message, Texts.T("Закрыть"));
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
        var settings = _settingsService.LoadSettings();
        ThemePicker.SelectedIndex = Math.Clamp(settings.ThemeIndex, 0, 2);
        LanguagePicker.SelectedIndex = Math.Clamp(settings.LanguageIndex, 0, 2);
        ReminderSwitch.IsToggled = settings.ReminderEnabled;
        ReminderTimePicker.Time = ReminderSchedule.ToTime(settings.ReminderMinutes);
        UpdateReminderStatus(settings);
        ApplyTheme(settings.ThemeIndex);
        _ready = true;
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

    /// <summary>Тема и язык — поверх свежих настроек: параметры тренировки правятся на вкладке «Тренировка».</summary>
    private void SaveControlsToSettings()
    {
        var settings = _settingsService.LoadSettings();
        settings.ThemeIndex = Math.Clamp(ThemePicker.SelectedIndex, 0, 2);
        settings.LanguageIndex = Math.Clamp(LanguagePicker.SelectedIndex, 0, 2);
        ApplyTheme(settings.ThemeIndex);
        _settingsService.SaveSettings(settings);
        SaveStatusLabel.Text = Texts.F("Сохранено {0:HH:mm}", DateTime.Now);
    }

    private async void ShareSettingsButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            // Настройки уходят одним профилем, вместе со своими напевами
            var settings = _settingsService.LoadSettings();
            var text = ProfileTransfer.Export(new[] { TrainingProfile.FromSettings(settings.ActiveProfileName, settings) }, LearningCatalog.CustomChants);
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
            var settings = _settingsService.LoadSettings();
            var profile = package.Profiles.FirstOrDefault(item => string.Equals(item.Name, settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
                          ?? package.Profiles[0];
            var name = settings.ActiveProfileName;
            profile.ApplyTo(settings);
            settings.ActiveProfileName = name;
            _settingsService.SaveSettings(settings);
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

    // ---------- обратная связь ----------

    private void FeedbackEditor_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        FeedbackSendButton.IsEnabled = !string.IsNullOrWhiteSpace(e.NewTextValue);
    }

    /// <summary>Сообщение разработчику: уходит только по кнопке; при сбое текст остаётся в поле — можно отправить ещё раз.</summary>
    private async void FeedbackSendButton_OnClicked(object sender, EventArgs e)
    {
        var text = FeedbackEditor.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        FeedbackSendButton.IsEnabled = false;
        FeedbackStatusLabel.IsVisible = true;
        FeedbackStatusLabel.ClearValue(Label.TextColorProperty);
        FeedbackStatusLabel.Text = Texts.T("Отправляем…");
        try
        {
            await FeedbackService.SendAsync(text, FeedbackContactEntry.Text ?? string.Empty, CurrentVersion.ToString(3));
            FeedbackEditor.Text = string.Empty;
            FeedbackStatusLabel.TextColor = (Color)Application.Current!.Resources["PrimaryDark"];
            FeedbackStatusLabel.Text = Texts.T("Спасибо! Сообщение отправлено разработчику.");
        }
        catch (Exception)
        {
            FeedbackStatusLabel.TextColor = (Color)Application.Current!.Resources["Danger"];
            FeedbackStatusLabel.Text = Texts.T("Не удалось отправить. Проверьте интернет и нажмите «Отправить» ещё раз — текст сохранён.");
            FeedbackSendButton.IsEnabled = true;
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

    // Пасхалка: нажатие на значок в шапке или на строку версии — «НЕВАДИМКА» азбукой на 175 знаков/мин
    private async void LogoMarkView_OnTapped(object? sender, EventArgs e) => await PlayEasterEggAsync();

    private async void VersionLabel_OnTapped(object? sender, TappedEventArgs e) => await PlayEasterEggAsync();

    private async Task PlayEasterEggAsync()
    {
        // Пасхалка не перебивает другой звук — например, прослушивание задания
        if (_audioPlayback.IsPlaying)
        {
            return;
        }

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
