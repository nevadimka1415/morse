using System.Text;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Models;
using MorseTrainer.Services;

using MorseTrainer.Mobile.Services;

namespace MorseTrainer.Mobile.Pages;

public partial class ProgressPage : ContentPage
{
    private readonly TrainingHistoryStore _historyStore;
    private readonly MobileSettingsService _settingsService;
    private bool _loadingGoal;

    public ProgressPage(TrainingHistoryStore historyStore, MobileSettingsService settingsService)
    {
        InitializeComponent();
        _historyStore = historyStore;
        _settingsService = settingsService;
        GoalPicker.ItemsSource = TrainingStatistics.DailyGoalChoices
            .Select(minutes => minutes == 0 ? Texts.T("без цели") : Texts.F("{0} мин", minutes)).ToList();
    }

    private void GoalPicker_OnChanged(object sender, EventArgs e)
    {
        if (_loadingGoal || GoalPicker.SelectedIndex < 0)
        {
            return;
        }

        var settings = _settingsService.LoadSettings();
        settings.DailyGoalMinutes = TrainingStatistics.DailyGoalChoices[GoalPicker.SelectedIndex];
        _settingsService.SaveSettings(settings);
        Refresh();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refresh();
    }

    // Одно устройство — один человек: прогресс всегда по всей истории, без выбора профиля
    private IReadOnlyList<TrainingRecord> LoadVisibleHistory() => _historyStore.Load();

    private async void ShareCsvButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"morse-history-{DateTime.Now:yyyy-MM-dd}.csv");
            // BOM нужен, чтобы Excel распознал UTF-8 и кириллицу
            await File.WriteAllTextAsync(path, TrainingStatistics.ToCsv(LoadVisibleHistory()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = Texts.T("История тренировок Morse Trainer"),
                File = new ShareFile(path)
            });
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    private void Refresh()
    {
        var records = LoadVisibleHistory();
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

        var exams = TrainingStatistics.Exams(records);
        ExamSeriesLabel.IsVisible = !exams.IsEmpty;
        ExamSeriesLabel.Text = exams.Describe();

        var goalMinutes = TrainingStatistics.ClampGoal(_settingsService.LoadSettings().DailyGoalMinutes);
        _loadingGoal = true;
        GoalPicker.SelectedIndex = Math.Max(0, TrainingStatistics.DailyGoalChoices.ToList().IndexOf(goalMinutes));
        _loadingGoal = false;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var goal = TrainingStatistics.Goal(records, goalMinutes, today);
        GoalLabel.Text = goal.Describe();
        GoalBar.IsVisible = goal.IsEnabled;
        GoalBar.Progress = goal.Progress;
        StreakLabel.Text = TrainingStatistics.Streak(records, today).Describe();

        // Первые две строки раздела — заголовок и подсказка
        while (DailyLayout.Children.Count > 2)
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
            var met = goal.IsEnabled && day.Minutes >= goal.GoalMinutes ? " ✓" : string.Empty;
            DailyLayout.Children.Add(new Label
            {
                Text = $"{day.Date:dd.MM}  {TextBar(day.AverageAccuracy / 100)}  {day.AverageAccuracy:0.#}% · {day.Sessions} · {day.Minutes:0}{met}",
                FontFamily = "monospace"
            });
        }

        while (SpeedLayout.Children.Count > 2)
        {
            SpeedLayout.Children.RemoveAt(SpeedLayout.Children.Count - 1);
        }

        var speeds = TrainingStatistics.SpeedByDay(records);
        if (speeds.Count == 0)
        {
            SpeedLayout.Children.Add(new Label { Text = Texts.T("Пока нет заданий с точностью от 90 %"), Opacity = 0.65, LineBreakMode = LineBreakMode.WordWrap });
        }

        // Полосы скорости — относительно лучшего дня, чтобы рост был виден и на малых скоростях
        var fastest = speeds.Count == 0 ? 1 : speeds.Max(item => item.CharactersPerMinute);
        foreach (var speed in speeds)
        {
            SpeedLayout.Children.Add(new Label
            {
                Text = $"{speed.Date:dd.MM}  {TextBar((double)speed.CharactersPerMinute / fastest)}  " + Texts.F("{0} зн/мин", speed.CharactersPerMinute),
                FontFamily = "monospace"
            });
        }

        HistoryView.ItemsSource = records.OrderByDescending(item => item.CompletedAt).Take(30).ToList();
    }

    private async void ShareHistoryButton_OnClicked(object sender, EventArgs e)
    {
        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"morse-history-{DateTime.Now:yyyy-MM-dd}.json");
            await File.WriteAllTextAsync(path, HistoryTransfer.Export(_historyStore.Load()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = Texts.T("История Morse Trainer"),
                File = new ShareFile(path, "application/json")
            });
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Не удалось поделиться"), exception.Message, Texts.T("Закрыть"));
        }
    }

    /// <summary>Импорт из файла (например, скачанного из мессенджера) или из текста в буфере обмена.</summary>
    private async void ImportHistoryButton_OnClicked(object sender, EventArgs e)
    {
        var fromFile = Texts.T("Из файла");
        var fromClipboard = Texts.T("Из буфера обмена");
        var choice = await DisplayActionSheetAsync(Texts.T("Импорт истории"), Texts.T("Отмена"), null, fromFile, fromClipboard);
        try
        {
            string? text = null;
            if (choice == fromFile)
            {
                var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Texts.T("Файл истории Morse Trainer (.json)") });
                if (picked is not null)
                {
                    await using var stream = await picked.OpenReadAsync();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    text = await reader.ReadToEndAsync();
                }
            }
            else if (choice == fromClipboard)
            {
                text = await Clipboard.Default.GetTextAsync();
            }

            if (text is null)
            {
                return;
            }

            var result = _historyStore.Merge(HistoryTransfer.Import(text));
            Refresh();
            await DisplayAlertAsync(Texts.T("Импорт истории"), result.Describe(), Texts.T("Понятно"));
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(Texts.T("Импорт истории"), exception.Message, Texts.T("Закрыть"));
        }
    }

    /// <summary>Текстовая полоса из десяти клеток для доли 0…1.</summary>
    private static string TextBar(double fraction)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 10);
        return new string('█', filled) + new string('░', 10 - filled);
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
