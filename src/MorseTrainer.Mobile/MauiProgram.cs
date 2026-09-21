using MorseTrainer.Mobile.Pages;
using MorseTrainer.Mobile.Services;
using System.Globalization;
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

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if ANDROID
        builder.Services.AddSingleton<IAudioPlaybackService, Platforms.Android.PlatformAudioPlaybackService>();
#elif IOS
        builder.Services.AddSingleton<IAudioPlaybackService, Platforms.iOS.PlatformAudioPlaybackService>();
#endif
        builder.Services.AddSingleton<MobileSettingsService>();
        builder.Services.AddSingleton(new TrainingHistoryStore(Path.Combine(FileSystem.AppDataDirectory, "history.json")));
        builder.Services.AddSingleton<VoicePackService>();
        builder.Services.AddSingleton<TrainingPage>();
        builder.Services.AddSingleton<LearningPage>();
        builder.Services.AddSingleton<KeyerPage>();
        builder.Services.AddSingleton<ProgressPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<AppShell>();
        return builder.Build();
    }
}
