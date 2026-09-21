using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Services;

using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class ProgressPage : ContentPage
{
    private readonly TrainingHistoryStore _historyStore;

    public ProgressPage(TrainingHistoryStore historyStore)
    {
        InitializeComponent();
        _historyStore = historyStore;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refresh();
    }

    private void Refresh()
    {
        var records = _historyStore.Load();
        var summary = TrainingStatistics.Summarize(records);
        if (summary.Sessions == 0)
        {
            SummaryLabel.Text = "Тренировок пока нет";
            DetailsLabel.Text = "Проверьте ответ в тренировке, и результат появится здесь.";
            ProblemSymbolsLabel.Text = string.Empty;
        }
        else
        {
            SummaryLabel.Text = $"{summary.Sessions} {Plural(summary.Sessions, "тренировка", "тренировки", "тренировок")} · средняя точность {summary.AverageAccuracy:0.#}%";
            DetailsLabel.Text = $"Последние 10: {summary.RecentAverageAccuracy:0.#}% · лучшая: {summary.BestAccuracy:0.#}% · " +
                                $"принято {summary.CorrectSymbols} из {summary.TotalSymbols} символов";
            var problems = TrainingStatistics.ProblemSymbols(records);
            ProblemSymbolsLabel.Text = problems.Count == 0
                ? "Ошибок в символах не было"
                : "Чаще всего ошибки: " + string.Join("  ", problems.Select(item => $"{item.Symbol} ×{item.Count}"));
        }

        while (DailyLayout.Children.Count > 1)
        {
            DailyLayout.Children.RemoveAt(DailyLayout.Children.Count - 1);
        }

        var days = TrainingStatistics.ByDay(records);
        if (days.Count == 0)
        {
            DailyLayout.Children.Add(new Label { Text = "Пока пусто", Opacity = 0.65 });
        }

        foreach (var day in days)
        {
            var filled = (int)Math.Round(day.AverageAccuracy / 10);
            var bar = new string('█', filled) + new string('░', 10 - filled);
            DailyLayout.Children.Add(new Label
            {
                Text = $"{day.Date:dd.MM}  {bar}  {day.AverageAccuracy:0.#}% · {day.Sessions}",
                FontFamily = "monospace"
            });
        }

        HistoryView.ItemsSource = records.OrderByDescending(item => item.CompletedAt).Take(30).ToList();
    }

    private async void ClearButton_OnClicked(object sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync("Очистить историю", "Удалить все записи о тренировках на этом устройстве?", "Удалить", "Отмена");
        if (!confirmed)
        {
            return;
        }

        _historyStore.Clear();
        Refresh();
    }

    private static string Plural(int count, string one, string few, string many)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        if (mod10 == 1 && mod100 != 11) return one;
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return few;
        return many;
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
}
