using MorseTrainer.Mobile.Pages;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile;

public sealed class AppShell : Shell
{
    // Вкладки «Прогресс» на телефоне нет: результаты сверяют с записью на бумаге; на её месте — проверка на слух
    public AppShell(TrainingPage trainingPage, LearningPage learningPage, QuizPage quizPage, KeyerPage keyerPage, SettingsPage settingsPage)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        // Цвета вкладок — явно для обеих тем (те же, что у MAUI по умолчанию): свои цвета MAUI после смены темы на ходу
        // берёт от прежней темы — невыбранные вкладки на светлой становились белыми на белом, на тёмной почти чёрными
        this.SetAppThemeColor(Shell.TabBarBackgroundColorProperty, Colors.White, Color.FromArgb("#1B3147"));
        this.SetAppThemeColor(Shell.TabBarTitleColorProperty, Color.FromArgb("#2C3E50"), Colors.White);
        this.SetAppThemeColor(Shell.TabBarForegroundColorProperty, Color.FromArgb("#2C3E50"), Colors.White);
        this.SetAppThemeColor(Shell.TabBarUnselectedColorProperty, Color.FromArgb("#666666"), Color.FromArgb("#BBC2C8"));
        var tabs = new TabBar();
        tabs.Items.Add(CreateTab(Texts.T("Тренировка"), "training", trainingPage));
        tabs.Items.Add(CreateTab(Texts.T("Обучение"), "learning", learningPage));
        tabs.Items.Add(CreateTab(Texts.T("На слух"), "quiz", quizPage));
        tabs.Items.Add(CreateTab(Texts.T("Передача"), "keyer", keyerPage));
        tabs.Items.Add(CreateTab(Texts.T("Настройки"), "settings", settingsPage));
        // Окно пересоздано (смена темы или шрифта) — открыть ту вкладку, где были (после смены темы — «Настройки»)
        if (App.TakeTabAfterRecreate() is { } route && tabs.Items.FirstOrDefault(tab => tab.CurrentItem?.Route == route) is { } current)
        {
            tabs.CurrentItem = current;
        }

        Items.Add(tabs);
    }

    private static ShellContent CreateTab(string title, string route, Page page)
    {
        return new ShellContent
        {
            Title = title,
            Route = route,
            ContentTemplate = new DataTemplate(() => page)
        };
    }
}
