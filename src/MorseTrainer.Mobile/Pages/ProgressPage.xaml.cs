using MorseTrainer.Domain;
using MorseTrainer.Localization;
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
            SummaryLabel.Text = Texts.T("Тренировок пока нет");
            DetailsLabel.Text = Texts.T("Проверьте ответ в тренировке, и результат появится здесь.");
            ProblemSymbolsLabel.Text = string.Empty;
        }
        else
        {
            SummaryLabel.Text = Texts.F("{0} {1} · средняя точность {2:0.#}%", summary.Sessions,
                Plural(summary.Sessions, Texts.T("тренировка"), Texts.T("тренировки"), Texts.T("тренировок")), summary.AverageAccuracy);
            DetailsLabel.Text = Texts.F("Последние 10: {0:0.#}% · лучшая: {1:0.#}% · ", summary.RecentAverageAccuracy, summary.BestAccuracy) +
                                Texts.F("принято {0} из {1} символов", summary.CorrectSymbols, summary.TotalSymbols);
            var problems = TrainingStatistics.ProblemSymbols(records);
            ProblemSymbolsLabel.Text = problems.Count == 0
                ? Texts.T("Ошибок в символах не было")
                : Texts.T("Чаще всего ошибки: ") + string.Join("  ", problems.Select(item => $"{item.Symbol} ×{item.Count}"));
        }

        while (DailyLayout.Children.Count > 1)
        {
            DailyLayout.Children.RemoveAt(DailyLayout.Children.Count - 1);
        }

        var days = TrainingStatistics.ByDay(records);
        if (days.Count == 0)
        {
            DailyLayout.Children.Add(new Label { Text = Texts.T("Пока пусто"), Opacity = 0.65 });
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
        var confirmed = await DisplayAlertAsync(Texts.T("Очистить историю"), Texts.T("Удалить все записи о тренировках на этом устройстве?"), Texts.T("Удалить"), Texts.T("Отмена"));
        if (!confirmed)
        {
            return;
        }

        _historyStore.Clear();
        Refresh();
    }

    private static string Plural(int count, string one, string few, string many)
    {
        if (Texts.IsEnglish)
        {
            return count == 1 ? one : many;
        }

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
