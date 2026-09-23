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

public sealed record DailyProgress(DateOnly Date, int Sessions, double AverageAccuracy);

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

    public static TrainingRecord CreateRecord(
        DateTime completedAt,
        string profileName,
        int charactersPerMinute,
        int groupCount,
        EvaluationResult result,
        bool isExam = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TrainingRecord
        {
            CompletedAt = completedAt,
            IsExam = isExam,
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

    /// <summary>Последние дни с тренировками (не больше days), от старых к новым.</summary>
    public static IReadOnlyList<DailyProgress> ByDay(IReadOnlyList<TrainingRecord> records, int days = 14)
    {
        ArgumentNullException.ThrowIfNull(records);
        return records
            .GroupBy(item => DateOnly.FromDateTime(item.CompletedAt))
            .Select(group => new DailyProgress(group.Key, group.Count(), Math.Round(group.Average(item => item.AccuracyPercent), 1)))
            .OrderByDescending(item => item.Date)
            .Take(Math.Max(1, days))
            .OrderBy(item => item.Date)
            .ToArray();
    }
}
