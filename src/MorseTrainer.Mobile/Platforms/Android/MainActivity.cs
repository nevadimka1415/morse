using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;

namespace MorseTrainer.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Тёмная или светлая тема, в которой нарисовано окно; null — окно ещё создаётся
    private UiMode? _night;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _night = Resources!.Configuration!.UiMode & UiMode.NightMask;
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        var night = newConfig.UiMode & UiMode.NightMask;
        if (_night is null || night == _night)
        {
            return;
        }

        // Тему сменили в «Настройках» (или в системе при «Системной»): уже нарисованные элементы Android сам не
        // перекрашивает — фон, текст и вкладки оставались бы в старой теме. Окно пересоздаётся, как при смене
        // шрифта, и открывается та же вкладка
        _night = night;
        App.RememberTabForRecreate();
        Recreate();
    }
}
