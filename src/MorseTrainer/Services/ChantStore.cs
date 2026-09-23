using System.IO;
using MorseTrainer.Domain;

namespace MorseTrainer.Services;

/// <summary>Свои напевы в файле chants.json в папке данных; пустой набор — файла нет.</summary>
public sealed class ChantStore
{
    private readonly string _path;

    public ChantStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public IReadOnlyDictionary<char, string> Load()
    {
        try
        {
            return File.Exists(_path) ? ChantBook.Deserialize(File.ReadAllText(_path)) : new Dictionary<char, string>();
        }
        catch
        {
            // Повреждённый файл не должен ломать обучение: остаются встроенные напевы
            return new Dictionary<char, string>();
        }
    }

    /// <summary>Задаёт свой напев символа; пустой напев возвращает встроенный. Возвращает весь набор после изменения.</summary>
    public IReadOnlyDictionary<char, string> Set(char symbol, string? chant)
    {
        symbol = char.ToUpperInvariant(symbol);
        var chants = new Dictionary<char, string>(Load());
        var normalized = ChantBook.Normalize(chant);
        if (normalized.Length == 0)
        {
            chants.Remove(symbol);
        }
        else
        {
            var error = ChantBook.Validate(symbol, normalized);
            if (error is not null)
            {
                throw new ArgumentException(error, nameof(chant));
            }

            chants[symbol] = normalized;
        }

        Save(chants);
        return chants;
    }

    public IReadOnlyDictionary<char, string> Merge(IReadOnlyDictionary<char, string> imported)
    {
        var chants = ChantBook.Merge(Load(), imported);
        Save(chants);
        return chants;
    }

    public void Save(IReadOnlyDictionary<char, string> chants)
    {
        ArgumentNullException.ThrowIfNull(chants);
        try
        {
            if (chants.Count == 0)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return;
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, ChantBook.Serialize(chants));
        }
        catch
        {
            // Ошибка записи не должна прерывать обучение
        }
    }
}
