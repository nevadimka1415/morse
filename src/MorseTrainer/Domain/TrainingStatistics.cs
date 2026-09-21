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

/// <summary>Сводки по истории тренировок. Чистые функции, одинаковые для Windows и телефона.</summary>
public static class TrainingStatistics
{
    public const int MaxRecords = 500;

    public static TrainingRecord CreateRecord(
        DateTime completedAt,
        string profileName,
        int charactersPerMinute,
        int groupCount,
        EvaluationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TrainingRecord
        {
            CompletedAt = completedAt,
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
