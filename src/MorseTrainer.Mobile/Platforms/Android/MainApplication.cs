using Android.App;
using Android.Runtime;
using AndroidX.AppCompat.App;
using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override void OnCreate()
    {
        base.OnCreate();
        // Тема из «Настроек» — до первого окна: иначе окно рисуется в системной теме и перекрашивается уже на ходу
        AppCompatDelegate.DefaultNightMode = new MobileSettingsService().LoadSettings().ThemeIndex switch
        {
            1 => AppCompatDelegate.ModeNightYes,
            2 => AppCompatDelegate.ModeNightNo,
            _ => AppCompatDelegate.ModeNightFollowSystem
        };
    }
}
