using System.IO;
using System.Text.Json;
using MorseTrainer.Domain;
using MorseTrainer.Models;

namespace MorseTrainer.Services;

/// <summary>История тренировок в JSON-файле. Путь задаёт платформа: LocalAppData на Windows, AppDataDirectory на телефоне.</summary>
public sealed class TrainingHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly string _path;

    public TrainingHistoryStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public IReadOnlyList<TrainingRecord> Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<TrainingRecord>();
            }

            var records = JsonSerializer.Deserialize<List<TrainingRecord>>(File.ReadAllText(_path), JsonOptions);
            return records is null ? Array.Empty<TrainingRecord>() : records.OrderBy(item => item.CompletedAt).ToArray();
        }
        catch
        {
            // Повреждённая история не должна ломать тренировку
            return Array.Empty<TrainingRecord>();
        }
    }

    public IReadOnlyList<TrainingRecord> Add(TrainingRecord record)
    {
        var history = TrainingStatistics.Add(Load(), record);
        Save(history);
        return history;
    }

    /// <summary>Сливает импортированную историю с текущей (без повторов) и сохраняет результат.</summary>
    public HistoryMergeResult Merge(IEnumerable<TrainingRecord> imported)
    {
        var result = HistoryTransfer.Merge(Load(), imported);
        Save(result.History);
        return result;
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // Нечего удалять или нет прав: история просто останется
        }
    }

    private void Save(IReadOnlyList<TrainingRecord> history)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(history, JsonOptions));
        }
        catch
        {
            // Ошибка записи не должна прерывать проверку ответа
        }
    }
}
