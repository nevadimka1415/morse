using System.Text.Encodings.Web;
using System.Text.Json;
using MorseTrainer.Localization;

namespace MorseTrainer.Domain;

/// <summary>
/// Свои напевы пользователя: проверка, приведение к виду «слог-слог» и JSON вида { "А": "ай-даа" }.
/// Один слог — один элемент кода: короткий слог — точка, протяжный (с долгой гласной) — тире.
/// </summary>
public static class ChantBook
{
    public const int MaxLength = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Нижний регистр, пробелы и любые тире — в дефис, без пустых слогов.</summary>
    public static string Normalize(string? chant)
    {
        if (string.IsNullOrWhiteSpace(chant))
        {
            return string.Empty;
        }

        var syllables = chant.Trim().ToLowerInvariant()
            .Split(new[] { '-', '–', '—', ' ', '_', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join('-', syllables);
    }

    /// <summary>null — напев подходит; иначе текст ошибки для пользователя.</summary>
    public static string? Validate(char symbol, string? chant)
    {
        if (!MorseAlphabet.TryGetCode(symbol, out var code))
        {
            return Texts.T("Для этого символа нет кода Морзе.");
        }

        var normalized = Normalize(chant);
        if (normalized.Length == 0)
        {
            return Texts.T("Напев пустой.");
        }

        if (normalized.Length > MaxLength)
        {
            return Texts.F("Напев длиннее {0} знаков.", MaxLength);
        }

        var syllables = normalized.Split('-').Length;
        return syllables == code.Length
            ? null
            : Texts.F("В напеве слогов: {0}, а в коде {1} знаков ({2}). На каждую точку и тире — свой слог через дефис.", syllables, code.Length, code);
    }

    /// <summary>Образец ритма для подсказки: «.-» → «ти-таа».</summary>
    public static string Pattern(string code) =>
        string.Join('-', code.Select(element => element == '.' ? Texts.T("ти") : Texts.T("таа")));

    public static string Serialize(IReadOnlyDictionary<char, string> chants)
    {
        ArgumentNullException.ThrowIfNull(chants);
        var map = chants
            .OrderBy(pair => pair.Key)
            .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
        return JsonSerializer.Serialize(map, JsonOptions);
    }

    /// <summary>Читает JSON напевов; неподходящие записи (нет кода, число слогов не совпадает) пропускаются.</summary>
    public static Dictionary<char, string> Deserialize(string? json)
    {
        var result = new Dictionary<char, string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        Dictionary<string, string>? map;
        try
        {
            map = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return result;
        }

        return Clean(map);
    }

    /// <summary>Проверяет пары «символ — напев» из импорта или файла и оставляет только подходящие.</summary>
    public static Dictionary<char, string> Clean(IEnumerable<KeyValuePair<string, string>>? pairs)
    {
        var result = new Dictionary<char, string>();
        foreach (var (key, value) in pairs ?? Array.Empty<KeyValuePair<string, string>>())
        {
            if (key is not { Length: 1 })
            {
                continue;
            }

            var symbol = char.ToUpperInvariant(key[0]);
            if (Validate(symbol, value) is null)
            {
                result[symbol] = Normalize(value);
            }
        }

        return result;
    }

    /// <summary>Импортированные напевы заменяют свои для тех же символов, остальные остаются.</summary>
    public static Dictionary<char, string> Merge(IReadOnlyDictionary<char, string> existing, IReadOnlyDictionary<char, string> imported)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(imported);
        var result = new Dictionary<char, string>(existing);
        foreach (var (symbol, chant) in imported)
        {
            result[symbol] = chant;
        }

        return result;
    }
}
