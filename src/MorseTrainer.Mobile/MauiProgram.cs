using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using MorseTrainer.Mobile.Pages;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Язык выбирается до создания страниц: их XAML читает переводы при разборе
        var startupSettings = new MobileSettingsService().LoadSettings();
        Texts.Apply((AppLanguage)Math.Clamp(startupSettings.LanguageIndex, 0, 2), CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        RegisterCrashHandlers();

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if ANDROID
        builder.Services.AddSingleton<IAudioPlaybackService, Platforms.Android.PlatformAudioPlaybackService>();
        builder.Services.AddSingleton<IReminderService, Platforms.Android.ReminderService>();
        builder.Services.AddSingleton<IAudioRecorderService, Platforms.Android.PlatformAudioRecorderService>();
#elif IOS
        builder.Services.AddSingleton<IAudioPlaybackService, Platforms.iOS.PlatformAudioPlaybackService>();
        builder.Services.AddSingleton<IReminderService, Platforms.iOS.ReminderService>();
        builder.Services.AddSingleton<IAudioRecorderService, Platforms.iOS.PlatformAudioRecorderService>();
#endif
        builder.Services.AddSingleton<MobileSettingsService>();
        builder.Services.AddSingleton(new TrainingHistoryStore(MobilePaths.HistoryFile));
        // Свои напевы пользователя подставляются в карточки обучения поверх встроенных
        var chantStore = new ChantStore(MobilePaths.ChantsFile);
        LearningCatalog.SetCustomChants(chantStore.Load());
        builder.Services.AddSingleton(chantStore);
        builder.Services.AddSingleton<VoicePackService>();
        builder.Services.AddSingleton<TrainingPage>();
        builder.Services.AddSingleton<LearningPage>();
        builder.Services.AddSingleton<KeyerPage>();
        builder.Services.AddSingleton<ProgressPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<AppShell>();
        return builder.Build();
    }

    // Необработанное исключение на телефоне уходит в crash.log в папке данных приложения;
    // в «О программе» его можно отправить разработчику кнопкой «Отправить отчёт»
    private static void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("Unhandled application exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
#if ANDROID
        // На Android исключения из Java-колбэков приходят через этот обработчик раньше, чем в AppDomain
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            WriteCrashLog("Android exception", e.Exception);
#endif
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        string version;
        string platform;
        try
        {
            version = AppInfo.Current.VersionString;
            platform = $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString} · {DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}";
        }
        catch
        {
            version = "?";
            platform = "?";
        }

        CrashReport.Write(MobilePaths.CrashLogFile, source, exception, version, platform);
    }
}
