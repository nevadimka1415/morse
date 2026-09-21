using MorseTrainer.Mobile.Pages;

namespace MorseTrainer.Mobile;

public sealed class AppShell : Shell
{
    public AppShell(TrainingPage trainingPage, LearningPage learningPage, ProgressPage progressPage, SettingsPage settingsPage)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Тренировка", "training", trainingPage));
        tabs.Items.Add(CreateTab("Обучение", "learning", learningPage));
        tabs.Items.Add(CreateTab("Прогресс", "progress", progressPage));
        tabs.Items.Add(CreateTab("Настройки", "settings", settingsPage));
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
