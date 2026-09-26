using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer;

/// <summary>Главное окно: вкладка «Прогресс» (видна с вводом ответа), история, цель дня, плашка и напоминание Windows.</summary>
public partial class MainWindow
{
    // ---------- Напоминание на Windows ----------

    private void ShowNudgeBanner()
    {
        var text = PracticeNudge.Banner(_history, DateOnly.FromDateTime(DateTime.Now), _lastPracticeAt);
        NudgeText.Text = text ?? " ";
        NudgeBanner.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NudgeStartButton_OnClick(object sender, RoutedEventArgs e)
    {
        NudgeBanner.Visibility = Visibility.Collapsed;
        MainTabs.SelectedIndex = 0;
        PlayButton.Focus();
    }

    private void NudgeCloseButton_OnClick(object sender, RoutedEventArgs e) => NudgeBanner.Visibility = Visibility.Collapsed;

    private void ReminderOption_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
            CheckReminder();
        }
    }

    /// <summary>Раз в минуту: пора ли показать уведомление о тренировке (только пока программа открыта).</summary>
    private void StartReminderTimer()
    {
        _reminderTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _reminderTimer.Tick -= ReminderTimer_OnTick;
        _reminderTimer.Tick += ReminderTimer_OnTick;
        _reminderTimer.Start();
    }

    private void ReminderTimer_OnTick(object? sender, EventArgs e) => CheckReminder();

    private void CheckReminder()
    {
        if (ReminderCheckBox.IsChecked != true)
        {
            return;
        }

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var trainedToday = PracticeNudge.PracticedOn(today, _history, _lastPracticeAt);
        if (!PracticeNudge.IsReminderDue(now, SelectedReminderMinutes, _reminderShownOn, trainedToday))
        {
            return;
        }

        _reminderShownOn = today;
        try
        {
            _trayReminder ??= new TrayReminder(this, ActivateFromReminder);
            // С серией от двух дней подряд — чтобы её не хотелось прерывать
            _trayReminder.Show("Morse Trainer", PracticeNudge.ReminderText(PracticeNudge.Streak(_history, _practiceDays, today).Current));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            // Уведомления недоступны (например, нет оболочки Windows) — остаётся плашка при следующем запуске
        }
    }

    private int SelectedReminderMinutes => PracticeNudge.TimeChoices[Math.Clamp(ReminderTimeCombo.SelectedIndex, 0, PracticeNudge.TimeChoices.Count - 1)];

    private void ActivateFromReminder()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        MainTabs.SelectedIndex = 0;
    }

    private bool _refreshingProfileFilter;

    /// <summary>История, видимая на вкладке «Прогресс»: вся или одного профиля из фильтра.</summary>
    private IReadOnlyList<TrainingRecord> VisibleHistory =>
        ProgressProfileCombo?.SelectedIndex > 0 && ProgressProfileCombo.SelectedItem is string name
            ? TrainingStatistics.ForProfile(_history, name)
            : _history;

    private void RefreshProfileFilter()
    {
        var items = new List<string> { Texts.T("Все профили") };
        items.AddRange(TrainingStatistics.ProfileNames(_history));
        var selected = ProgressProfileCombo.SelectedItem as string;
        _refreshingProfileFilter = true;
        ProgressProfileCombo.ItemsSource = items;
        var index = selected is null ? -1 : items.FindIndex(item => string.Equals(item, selected, StringComparison.OrdinalIgnoreCase));
        ProgressProfileCombo.SelectedIndex = index > 0 ? index : 0;
        _refreshingProfileFilter = false;
    }

    private void ProgressProfileCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingProfileFilter && _windowLoaded)
        {
            RefreshProgress();
        }
    }

    private void ExportCsvButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Экспорт CSV"),
            Filter = Texts.T("Таблица CSV (*.csv)|*.csv"),
            FileName = $"morse-history-{DateTime.Now:yyyy-MM-dd}.csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            // BOM нужен, чтобы Excel распознал UTF-8 и кириллицу
            File.WriteAllText(dialog.FileName, TrainingStatistics.ToCsv(VisibleHistory), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Экспорт CSV"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Texts.T("Экспорт истории"),
            Filter = Texts.T("История Morse Trainer (*.json)|*.json"),
            FileName = $"morse-history-{DateTime.Now:yyyy-MM-dd}.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, HistoryTransfer.Export(_history), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Экспорт истории"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Texts.T("Импорт истории"),
            Filter = Texts.T("История Morse Trainer (*.json)|*.json|Все файлы (*.*)|*.*")
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = _historyStore.Merge(HistoryTransfer.Import(File.ReadAllText(dialog.FileName)));
            _history = result.History;
            RefreshProgress();
            MessageBox.Show(this, result.Describe(), Texts.T("Импорт истории"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, Texts.T("Импорт истории"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshProgress()
    {
        RefreshProfileFilter();
        var history = VisibleHistory;
        var summary = TrainingStatistics.Summarize(history);
        ProgressSessionsText.Text = summary.Sessions.ToString(CultureInfo.InvariantCulture);
        ProgressAverageText.Text = summary.Sessions == 0 ? "—" : $"{summary.AverageAccuracy:0.#}%";
        ProgressBestText.Text = summary.Sessions == 0 ? "—" : $"{summary.BestAccuracy:0.#}%";
        ProgressSymbolsText.Text = summary.Sessions == 0 ? "—" : $"{summary.CorrectSymbols} / {summary.TotalSymbols}";
        var problems = TrainingStatistics.ProblemSymbols(history);
        HistoryProblemSymbolsText.Text = problems.Count == 0
            ? "—"
            : string.Join("  ", problems.Select(item => $"{item.Symbol} ×{item.Count}"));
        ExamSeriesText.Text = TrainingStatistics.Exams(history).Describe();
        HistoryListView.ItemsSource = history
            .OrderByDescending(item => item.CompletedAt)
            .Take(50)
            .Select(item => new HistoryRow(
                item.CompletedAt.ToString("dd.MM.yy HH:mm", CultureInfo.CurrentCulture),
                (item.IsExam ? Texts.F("{0} · экзамен", item.ProfileName) : item.ProfileName) +
                (item.CourseStep > 0 ? Texts.F(" · шаг {0}", item.CourseStep) : string.Empty),
                Texts.F("{0} зн/мин", item.CharactersPerMinute),
                item.GroupCount.ToString(CultureInfo.InvariantCulture),
                $"{item.AccuracyPercent:0.#}%",
                item.ProblemSymbols.Length == 0 ? "—" : string.Join(" ", item.ProblemSymbols.Distinct())))
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var goal = TrainingStatistics.Goal(history, SelectedDailyGoal, today);
        GoalText.Text = goal.Describe();
        GoalText.Foreground = (Brush)FindResource(goal.IsMet ? "PrimaryBrush" : "TextBrush");
        GoalProgress.Value = goal.Progress * 100;
        GoalProgress.Visibility = goal.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        StreakText.Text = TrainingStatistics.Streak(history, today).Describe();

        DailyBarsPanel.Children.Clear();
        var days = TrainingStatistics.ByDay(history);
        if (days.Count == 0)
        {
            DailyBarsPanel.Children.Add(new TextBlock { Text = Texts.T("Пока пусто"), Foreground = (Brush)FindResource("MutedTextBrush") });
        }

        foreach (var day in days)
        {
            var met = goal.IsEnabled && day.Minutes >= goal.GoalMinutes ? " ✓" : string.Empty;
            AddBarRow(DailyBarsPanel, day.Date, day.AverageAccuracy / 100,
                $"{day.AverageAccuracy:0.#}% · {day.Sessions} · {day.Minutes:0}{met}");
        }

        SpeedBarsPanel.Children.Clear();
        var speeds = TrainingStatistics.SpeedByDay(history);
        if (speeds.Count == 0)
        {
            SpeedBarsPanel.Children.Add(new TextBlock
            {
                Text = Texts.T("Пока нет заданий с точностью от 90 %"),
                Foreground = (Brush)FindResource("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        // Полосы скорости — относительно лучшего дня в окне, чтобы рост был виден и на малых скоростях
        var fastest = speeds.Count == 0 ? 1 : speeds.Max(item => item.CharactersPerMinute);
        foreach (var speed in speeds)
        {
            AddBarRow(SpeedBarsPanel, speed.Date, (double)speed.CharactersPerMinute / fastest, Texts.F("{0} зн/мин", speed.CharactersPerMinute));
        }
    }

    private int SelectedDailyGoal => TrainingStatistics.DailyGoalChoices[Math.Clamp(DailyGoalCombo.SelectedIndex, 0, TrainingStatistics.DailyGoalChoices.Count - 1)];

    /// <summary>Строка полосы: дата, заполненная доля 0…1 и подпись справа.</summary>
    private void AddBarRow(Panel panel, DateOnly date, double fraction, string valueText)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        var label = new TextBlock { Text = date.ToString("dd.MM", CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center };
        // Доля задаётся звёздными колонками: полоса тянется вместе с окном
        var fill = new Border { Background = (Brush)FindResource("PrimaryBrush"), CornerRadius = new CornerRadius(5), MinWidth = 4 };
        var bar = new Grid();
        var share = Math.Clamp(fraction, 0.02, 1);
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share + 0.0001, GridUnitType.Star) });
        bar.Children.Add(fill);
        var track = new Border
        {
            Background = (Brush)FindResource("CardAltBrush"),
            CornerRadius = new CornerRadius(5),
            Height = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Child = bar
        };
        var value = new TextBlock
        {
            Text = valueText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = (Brush)FindResource("MutedTextBrush")
        };
        Grid.SetColumn(track, 1);
        Grid.SetColumn(value, 2);
        row.Children.Add(label);
        row.Children.Add(track);
        row.Children.Add(value);
        panel.Children.Add(row);
    }

    private void DailyGoalCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_windowLoaded)
        {
            _settingsService.Save(ReadSettings());
            RefreshProgress();
        }
    }

    private void ClearHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Texts.T("Удалить все записи о тренировках на этом компьютере?"), Texts.T("Очистить историю"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _historyStore.Clear();
        _history = Array.Empty<TrainingRecord>();
        RefreshProgress();
    }

    // Колонки истории делят ширину таблицы по долям — без горизонтальной прокрутки на маленьком окне
    private static readonly double[] HistoryColumnShares = { 0.21, 0.22, 0.13, 0.09, 0.14, 0.21 };

    private void HistoryListView_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || HistoryListView.View is not GridView view)
        {
            return;
        }

        var available = Math.Max(280, HistoryListView.ActualWidth - SystemParameters.VerticalScrollBarWidth - 14);
        for (var index = 0; index < view.Columns.Count && index < HistoryColumnShares.Length; index++)
        {
            view.Columns[index].Width = Math.Floor(available * HistoryColumnShares[index]);
        }
    }
}

/// <summary>Строка таблицы истории на вкладке «Прогресс».</summary>
public sealed record HistoryRow(string When, string Profile, string Speed, string Groups, string Accuracy, string Errors);
