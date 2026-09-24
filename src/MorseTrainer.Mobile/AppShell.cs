using MorseTrainer.Mobile.Pages;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile;

public sealed class AppShell : Shell
{
    // Вкладки «Прогресс» на телефоне нет: результаты сверяют с записью на бумаге; на её месте — проверка на слух
    public AppShell(TrainingPage trainingPage, LearningPage learningPage, QuizPage quizPage, KeyerPage keyerPage, SettingsPage settingsPage)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        var tabs = new TabBar();
        tabs.Items.Add(CreateTab(Texts.T("Тренировка"), "training", trainingPage));
        tabs.Items.Add(CreateTab(Texts.T("Обучение"), "learning", learningPage));
        tabs.Items.Add(CreateTab(Texts.T("На слух"), "quiz", quizPage));
        tabs.Items.Add(CreateTab(Texts.T("Передача"), "keyer", keyerPage));
        tabs.Items.Add(CreateTab(Texts.T("Настройки"), "settings", settingsPage));
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
