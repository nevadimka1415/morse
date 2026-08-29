using MorseTrainer.Mobile.Pages;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if ANDROID
        builder.Services.AddSingleton<IAudioPlaybackService, Platforms.Android.PlatformAudioPlaybackService>();
#elif IOS
        builder.Services.AddSingleton<IAudioPlaybackService, Platforms.iOS.PlatformAudioPlaybackService>();
#endif
        builder.Services.AddSingleton<MobileSettingsService>();
        builder.Services.AddSingleton<VoicePackService>();
        builder.Services.AddSingleton<TrainingPage>();
        builder.Services.AddSingleton<LearningPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<AppShell>();
        return builder.Build();
    }
}
