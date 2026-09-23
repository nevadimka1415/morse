using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>Итог слияния истории: новая история, сколько записей добавлено, сколько пропущено как повторы и сколько старых вытеснено лимитом.</summary>
public sealed record HistoryMergeResult(IReadOnlyList<TrainingRecord> History, int Added, int Duplicates, int Trimmed)
{
    public string Describe()
    {
        var text = Texts.F("Добавлено записей: {0}, пропущено повторов: {1}.", Added, Duplicates);
        return Trimmed > 0 ? text + " " + Texts.F("Самые старые записи сверх {0} удалены: {1}.", TrainingStatistics.MaxRecords, Trimmed) : text;
    }
}

/// <summary>
/// Перенос истории тренировок между устройствами: JSON с конвертом, как у профилей. Слияние по времени без дублей:
/// одна и та же тренировка, пришедшая дважды, узнаётся по времени до секунды, профилю и счётчикам символов.
/// </summary>
public static class HistoryTransfer
{
    public const int FormatVersion = 1;
    public const string Kind = "history";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // Кириллица в именах профилей и символах ошибок остаётся читаемой, а не \u-кодами
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class Envelope
    {
        public string App { get; set; } = "MorseTrainer";
        public string Kind { get; set; } = HistoryTransfer.Kind;
        public int Version { get; set; } = FormatVersion;
        public DateTime ExportedAt { get; set; }
        public List<TrainingRecord>? Records { get; set; }
        // Только для распознавания: файл профилей по ошибке выбран вместо истории
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Profiles { get; set; }
    }

    public static string Export(IEnumerable<TrainingRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var envelope = new Envelope { ExportedAt = DateTime.Now, Records = records.OrderBy(item => item.CompletedAt).ToList() };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    /// <summary>Принимает конверт Export или просто массив записей; значения приводятся к допустимым.</summary>
    public static IReadOnlyList<TrainingRecord> Import(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException(Texts.T("Текст пустой."));
        }

        var trimmed = text.Trim();
        List<TrainingRecord>? records;
        try
        {
            if (trimmed.StartsWith('['))
            {
                records = JsonSerializer.Deserialize<List<TrainingRecord>>(trimmed, JsonOptions);
            }
            else
            {
                var envelope = JsonSerializer.Deserialize<Envelope>(trimmed, JsonOptions);
                if (envelope?.Records is null && envelope?.Profiles is not null)
                {
                    throw new FormatException(Texts.T("Это профили, а не история: импортируйте их в разделе профилей."));
                }

                records = envelope?.Records;
            }
        }
        catch (JsonException exception)
        {
            throw new FormatException(Texts.T("Это не история Morse Trainer."), exception);
        }

        var result = (records ?? new List<TrainingRecord>())
            .Where(record => record is not null && record.CompletedAt != default)
            .Select(Normalize)
            .ToArray();
        if (result.Length == 0)
        {
            throw new FormatException(Texts.T("В тексте нет записей истории."));
        }

        return result;
    }

    /// <summary>Объединяет историю: повторы пропускаются, всё сортируется по времени, лишнее сверх MaxRecords отрезается со старой стороны.</summary>
    public static HistoryMergeResult Merge(IEnumerable<TrainingRecord> existing, IEnumerable<TrainingRecord> imported)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(imported);
        var merged = existing.ToList();
        var keys = merged.Select(Key).ToHashSet();
        var added = 0;
        var duplicates = 0;
        foreach (var record in imported)
        {
            if (keys.Add(Key(record)))
            {
                merged.Add(record);
                added++;
            }
            else
            {
                duplicates++;
            }
        }

        var ordered = merged.OrderBy(item => item.CompletedAt).ToList();
        var trimmed = Math.Max(0, ordered.Count - TrainingStatistics.MaxRecords);
        if (trimmed > 0)
        {
            ordered.RemoveRange(0, trimmed);
        }

        return new HistoryMergeResult(ordered, added, duplicates, trimmed);
    }

    private static string Key(TrainingRecord record)
    {
        var second = record.CompletedAt.Ticks / TimeSpan.TicksPerSecond;
        return FormattableString.Invariant($"{second}|{(record.ProfileName ?? string.Empty).Trim().ToUpperInvariant()}|{record.TotalCount}|{record.CorrectCount}|{record.IsExam}");
    }

    private static TrainingRecord Normalize(TrainingRecord record)
    {
        var total = Math.Max(0, record.TotalCount);
        return new TrainingRecord
        {
            CompletedAt = record.CompletedAt,
            IsExam = record.IsExam,
            ProfileName = string.IsNullOrWhiteSpace(record.ProfileName) ? "Основной" : TrainingProfile.NormalizeName(record.ProfileName),
            CharactersPerMinute = Math.Clamp(record.CharactersPerMinute, 0, 300),
            GroupCount = Math.Clamp(record.GroupCount, 0, 100),
            TotalCount = total,
            CorrectCount = Math.Clamp(record.CorrectCount, 0, total),
            AccuracyPercent = Math.Clamp(record.AccuracyPercent, 0, 100),
            ProblemSymbols = new string((record.ProblemSymbols ?? string.Empty).Where(symbol => MorseAlphabet.TryGetCode(symbol, out _)).ToArray()),
            DurationSeconds = Math.Clamp(record.DurationSeconds, 0, TrainingStatistics.MaxTaskMinutes * 60)
        };
    }
}
