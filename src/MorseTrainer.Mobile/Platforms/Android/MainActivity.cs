using Android.App;
using Android.Content.PM;

namespace MorseTrainer.Mobile;

// UiMode (тёмная/светлая тема) намеренно НЕ в ConfigurationChanges: при смене темы — в «Настройках» или в системе
// при «Системной» — Android сам пересоздаёт окно, и всё рисуется заново в новой теме. Если менять тему в том же окне,
// уже нарисованное остаётся в старой теме, а MAUI перекрашивает поля ввода по ресурсам, которые Android только что
// заменил («AssetManager has been destroyed») — приложение падало.
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnStop()
    {
        // Окно пересоздаётся (тема, размер шрифта) — в новом окне открыть ту же вкладку, а не первую
        if (IsChangingConfigurations)
        {
            App.RememberTabForRecreate();
        }

        base.OnStop();
    }
}
