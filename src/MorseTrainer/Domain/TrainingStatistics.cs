using System.Globalization;
using System.Text;
using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

public sealed record HistorySummary(
    int Sessions,
    double AverageAccuracy,
    double RecentAverageAccuracy,
    double BestAccuracy,
    int TotalSymbols,
    int CorrectSymbols);

public sealed record DailyProgress(DateOnly Date, int Sessions, double AverageAccuracy, double Minutes = 0);

/// <summary>Лучшая скорость дня среди заданий с точностью от SpeedAccuracyThreshold.</summary>
public sealed record DailySpeed(DateOnly Date, int CharactersPerMinute);

/// <summary>Дни подряд с тренировкой: текущая серия (до сегодня или вчера) и рекорд.</summary>
public sealed record PracticeStreak(int Current, int Best, bool TrainedToday)
{
    public string Describe() => Texts.F("Дней подряд: {0} · рекорд {1}", Current, Best) +
                                (Current > 0 && !TrainedToday ? Texts.T(" · сегодня ещё не занимались") : string.Empty);
}

/// <summary>Цель «минут в день» и сколько набрано сегодня.</summary>
public sealed record DailyGoal(int GoalMinutes, double TodayMinutes)
{
    public bool IsEnabled => GoalMinutes > 0;

    public bool IsMet => IsEnabled && TodayMinutes >= GoalMinutes;

    /// <summary>Доля выполнения 0…1 для полосы.</summary>
    public double Progress => IsEnabled ? Math.Min(1, TodayMinutes / GoalMinutes) : 0;

    public string Describe() => !IsEnabled
        ? Texts.F("Сегодня: {0:0.#} мин · цель не задана", TodayMinutes)
        : IsMet
            ? Texts.F("Сегодня: {0:0.#} из {1} мин — цель выполнена ✓", TodayMinutes, GoalMinutes)
            : Texts.F("Сегодня: {0:0.#} из {1} мин", TodayMinutes, GoalMinutes);
}

public sealed record ProblemSymbolCount(char Symbol, int Count);

/// <summary>Серия последних экзаменов (от старых к новым) и тренд точности в процентных пунктах за экзамен.</summary>
public sealed record ExamSeries(IReadOnlyList<TrainingRecord> Exams, double TrendPerExam)
{
    public bool IsEmpty => Exams.Count == 0;

    public string Describe()
    {
        if (IsEmpty)
        {
            return Texts.T("Экзаменов пока нет");
        }

        var values = string.Join(" · ", Exams.Select(item => item.AccuracyPercent.ToString("0.#", CultureInfo.CurrentCulture) + "%"));
        var text = Texts.F("Последние экзамены: {0}", values);
        if (Exams.Count < 2)
        {
            return text;
        }

        // Меньше половины пункта за экзамен — шум, а не тренд
        var trend = Math.Abs(TrendPerExam) < 0.5
            ? Texts.T("тренд → ровно")
            : TrendPerExam > 0
                ? Texts.F("тренд ↑ +{0:0.#} п.п. за экзамен", TrendPerExam)
                : Texts.F("тренд ↓ −{0:0.#} п.п. за экзамен", -TrendPerExam);
        return text + " — " + trend;
    }
}

/// <summary>Сводки по истории тренировок. Чистые функции, одинаковые для Windows и телефона.</summary>
public static class TrainingStatistics
{
    public const int MaxRecords = 500;

    /// <summary>Потолок времени одного задания: забытое на час задание не засчитывается часом тренировки.</summary>
    public const int MaxTaskMinutes = 30;

    /// <summary>Скорость дня считается только по заданиям с такой точностью и выше.</summary>
    public const double SpeedAccuracyThreshold = 90;

    /// <summary>Варианты цели на день в минутах; 0 — без цели.</summary>
    public static readonly IReadOnlyList<int> DailyGoalChoices = new[] { 0, 5, 10, 15, 20, 30, 45, 60 };

    public static int ClampGoal(int minutes) => Math.Clamp(minutes, 0, DailyGoalChoices[^1]);

    public static TrainingRecord CreateRecord(
        DateTime completedAt,
        string profileName,
        int charactersPerMinute,
        int groupCount,
        EvaluationResult result,
        bool isExam = false,
        TimeSpan? duration = null,
        int courseStep = 0)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TrainingRecord
        {
            CompletedAt = completedAt,
            IsExam = isExam,
            CourseStep = Math.Max(0, courseStep),
            DurationSeconds = duration is null ? 0 : (int)Math.Round(Math.Clamp(duration.Value.TotalSeconds, 0, MaxTaskMinutes * 60)),
            ProfileName = string.IsNullOrWhiteSpace(profileName) ? "Основной" : profileName.Trim(),
            CharactersPerMinute = charactersPerMinute,
            GroupCount = groupCount,
            TotalCount = result.TotalCount,
            CorrectCount = result.CorrectCount,
            AccuracyPercent = Math.Round(result.AccuracyPercent, 1),
            ProblemSymbols = new string(result.Mistakes
                .Where(mistake => mistake.Expected != '∅')
                .Select(mistake => mistake.Expected)
                .ToArray())
        };
    }

    /// <summary>Добавляет запись и оставляет не больше MaxRecords самых свежих.</summary>
    public static IReadOnlyList<TrainingRecord> Add(IEnumerable<TrainingRecord> history, TrainingRecord record)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(record);
        var list = history.Append(record).OrderBy(item => item.CompletedAt).ToList();
        if (list.Count > MaxRecords)
        {
            list.RemoveRange(0, list.Count - MaxRecords);
        }

        return list;
    }

    public static HistorySummary Summarize(IReadOnlyList<TrainingRecord> records, int recentCount = 10)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return new HistorySummary(0, 0, 0, 0, 0, 0);
        }

        var ordered = records.OrderBy(item => item.CompletedAt).ToList();
        var recent = ordered.TakeLast(Math.Max(1, recentCount)).ToList();
        return new HistorySummary(
            ordered.Count,
            Math.Round(ordered.Average(item => item.AccuracyPercent), 1),
            Math.Round(recent.Average(item => item.AccuracyPercent), 1),
            Math.Round(ordered.Max(item => item.AccuracyPercent), 1),
            ordered.Sum(item => item.TotalCount),
            ordered.Sum(item => item.CorrectCount));
    }

    public static IReadOnlyList<ProblemSymbolCount> ProblemSymbols(IReadOnlyList<TrainingRecord> records, int top = 8)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .SelectMany(item => item.ProblemSymbols)
            .GroupBy(symbol => symbol)
            .Select(group => new ProblemSymbolCount(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Symbol)
            .Take(Math.Max(1, top))
            .ToArray();
    }

    /// <summary>Последние count экзаменов и тренд: наклон прямой по методу наименьших квадратов.</summary>
    public static ExamSeries Exams(IReadOnlyList<TrainingRecord> records, int count = 5)
    {
        ArgumentNullException.ThrowIfNull(records);
        var exams = records
            .Where(item => item.IsExam)
            .OrderBy(item => item.CompletedAt)
            .TakeLast(Math.Max(1, count))
            .ToArray();
        return new ExamSeries(exams, Slope(exams.Select(item => item.AccuracyPercent).ToArray()));
    }

    private static double Slope(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var meanX = (values.Count - 1) / 2.0;
        var meanY = values.Average();
        double numerator = 0;
        double denominator = 0;
        for (var index = 0; index < values.Count; index++)
        {
            numerator += (index - meanX) * (values[index] - meanY);
            denominator += (index - meanX) * (index - meanX);
        }

        return Math.Round(numerator / denominator, 1);
    }

    /// <summary>Имена профилей, встречающиеся в истории, по алфавиту.</summary>
    public static IReadOnlyList<string> ProfileNames(IReadOnlyList<TrainingRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .Select(item => item.ProfileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>Записи одного профиля (без учёта регистра); null или пустое имя — вся история.</summary>
    public static IReadOnlyList<TrainingRecord> ForProfile(IReadOnlyList<TrainingRecord> records, string? profileName)
    {
        ArgumentNullException.ThrowIfNull(records);
        return string.IsNullOrWhiteSpace(profileName)
            ? records
            : records.Where(item => string.Equals(item.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>
    /// Таблица CSV для Excel: разделитель «;», десятичный знак текущей культуры, даты ISO.
    /// Файл лучше сохранять в UTF-8 с BOM, чтобы Excel распознал кириллицу.
    /// </summary>
    public static string ToCsv(IReadOnlyList<TrainingRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var culture = CultureInfo.CurrentCulture;
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', new[]
        {
            Texts.T("Дата"), Texts.T("Профиль"), Texts.T("Экзамен"), Texts.T("Скорость (зн/мин)"), Texts.T("Групп"),
            Texts.T("Символов"), Texts.T("Верно"), Texts.T("Точность (%)"), Texts.T("Ошибки")
        }));
        foreach (var item in records.OrderBy(item => item.CompletedAt))
        {
            builder.AppendLine(string.Join(';', new[]
            {
                item.CompletedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Escape(item.ProfileName),
                item.IsExam ? Texts.T("да") : string.Empty,
                item.CharactersPerMinute.ToString(CultureInfo.InvariantCulture),
                item.GroupCount.ToString(CultureInfo.InvariantCulture),
                item.TotalCount.ToString(CultureInfo.InvariantCulture),
                item.CorrectCount.ToString(CultureInfo.InvariantCulture),
                item.AccuracyPercent.ToString("0.#", culture),
                Escape(item.ProblemSymbols)
            }));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var needsQuotes = value.Contains(';') || value.Contains('"') || value.Contains('\n');
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>
    /// Минуты тренировки по записи: замер от создания задания до проверки, а для старых записей без замера —
    /// время звучания задания (символов / скорость).
    /// </summary>
    public static double PracticeMinutes(TrainingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.DurationSeconds > 0)
        {
            return record.DurationSeconds / 60.0;
        }

        return record.CharactersPerMinute > 0 ? (double)record.TotalCount / record.CharactersPerMinute : 0;
    }

    /// <summary>Лучшая скорость по дням (задания с точностью от SpeedAccuracyThreshold), последние days дней с такими заданиями.</summary>
    public static IReadOnlyList<DailySpeed> SpeedByDay(IReadOnlyList<TrainingRecord> records, int days = 14)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .Where(item => item.AccuracyPercent >= SpeedAccuracyThreshold && item.CharactersPerMinute > 0)
            .GroupBy(item => DateOnly.FromDateTime(item.CompletedAt))
            .Select(group => new DailySpeed(group.Key, group.Max(item => item.CharactersPerMinute)))
            .OrderByDescending(item => item.Date)
            .Take(Math.Max(1, days))
            .OrderBy(item => item.Date)
            .ToArray();
    }

    /// <summary>Серия дней подряд: считается до сегодня, а если сегодня ещё не занимались — до вчера.</summary>
    public static PracticeStreak Streak(IReadOnlyList<TrainingRecord> records, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(records);
        return Streak(records.Select(item => DateOnly.FromDateTime(item.CompletedAt)), today);
    }

    /// <summary>Серия по дням занятий (история и занятия без записи в ней — на бумаге, «На слух»).</summary>
    public static PracticeStreak Streak(IEnumerable<DateOnly> practiceDays, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(practiceDays);
        var days = practiceDays.ToHashSet();
        var best = 0;
        foreach (var day in days)
        {
            // Начало серии — день, перед которым тренировки не было
            if (days.Contains(day.AddDays(-1)))
            {
                continue;
            }

            var length = 1;
            while (days.Contains(day.AddDays(length)))
            {
                length++;
            }

            best = Math.Max(best, length);
        }

        var trainedToday = days.Contains(today);
        var cursor = trainedToday ? today : today.AddDays(-1);
        var current = 0;
        while (days.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }

        return new PracticeStreak(current, best, trainedToday);
    }

    public static DailyGoal Goal(IReadOnlyList<TrainingRecord> records, int goalMinutes, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(records);
        var minutes = records.Where(item => DateOnly.FromDateTime(item.CompletedAt) == today).Sum(PracticeMinutes);
        return new DailyGoal(ClampGoal(goalMinutes), Math.Round(minutes, 1));
    }

    /// <summary>Последние дни с тренировками (не больше days), от старых к новым.</summary>
    public static IReadOnlyList<DailyProgress> ByDay(IReadOnlyList<TrainingRecord> records, int days = 14)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .GroupBy(item => DateOnly.FromDateTime(item.CompletedAt))
            .Select(group => new DailyProgress(group.Key, group.Count(), Math.Round(group.Average(item => item.AccuracyPercent), 1),
                Math.Round(group.Sum(PracticeMinutes), 1)))
            .OrderByDescending(item => item.Date)
            .Take(Math.Max(1, days))
            .OrderBy(item => item.Date)
            .ToArray();
    }
}
