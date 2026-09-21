using System.Text.Encodings.Web;
using System.Text.Json;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>Перенос профилей между устройствами: текст JSON, одинаковый для Windows и телефона.</summary>
public static class ProfileTransfer
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Кириллица в именах профилей остаётся читаемой, а не \u-кодами
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class Envelope
    {
        public string App { get; set; } = "MorseTrainer";
        public int Version { get; set; } = FormatVersion;
        public DateTime ExportedAt { get; set; }
        public List<TrainingProfile> Profiles { get; set; } = new();
    }

    public static string Export(IEnumerable<TrainingProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        return JsonSerializer.Serialize(new Envelope { ExportedAt = DateTime.Now, Profiles = profiles.ToList() }, JsonOptions);
    }

    /// <summary>Принимает конверт Export или просто массив профилей; значения приводятся к допустимым диапазонам.</summary>
    public static IReadOnlyList<TrainingProfile> Import(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Текст пустой.");
        }

        var trimmed = text.Trim();
        List<TrainingProfile>? profiles;
        try
        {
            profiles = trimmed.StartsWith('[')
                ? JsonSerializer.Deserialize<List<TrainingProfile>>(trimmed, JsonOptions)
                : JsonSerializer.Deserialize<Envelope>(trimmed, JsonOptions)?.Profiles;
        }
        catch (JsonException exception)
        {
            throw new FormatException("Это не профили Morse Trainer.", exception);
        }

        if (profiles is null || profiles.Count == 0)
        {
            throw new FormatException("В тексте нет профилей.");
        }

        var result = new List<TrainingProfile>();
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                continue;
            }

            profile.Name = TrainingProfile.NormalizeName(profile.Name);
            Clamp(profile);
            result.Add(profile);
        }

        if (result.Count == 0)
        {
            throw new FormatException("У профилей нет названий.");
        }

        return result;
    }

    /// <summary>Импортированные профили заменяют существующие с тем же именем (без учёта регистра), остальные остаются.</summary>
    public static IReadOnlyList<TrainingProfile> Merge(IEnumerable<TrainingProfile> existing, IEnumerable<TrainingProfile> imported)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(imported);
        var merged = new Dictionary<string, TrainingProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in existing.Concat(imported))
        {
            merged[profile.Name] = profile;
        }

        return merged.Values.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static void Clamp(TrainingProfile profile)
    {
        profile.AlphabetIndex = Math.Clamp(profile.AlphabetIndex, 0, 2);
        profile.ContentModeIndex = Math.Clamp(profile.ContentModeIndex, 0, (int)ContentMode.Koch);
        profile.GroupCount = Math.Clamp(profile.GroupCount, 1, 100);
        profile.CharactersPerMinute = Math.Clamp(profile.CharactersPerMinute, 20, 300);
        profile.FrequencyHz = Math.Clamp(profile.FrequencyHz, 300, 1200);
        profile.VolumePercent = Math.Clamp(profile.VolumePercent, 0, 100);
        profile.CharacterGapUnits = Math.Clamp(profile.CharacterGapUnits, 3, 20);
        profile.GroupGapUnits = Math.Clamp(profile.GroupGapUnits, 7, 30);
        profile.StartPauseUnits = Math.Clamp(profile.StartPauseUnits, 7, 60);
        profile.KochLevel = Math.Clamp(profile.KochLevel, KochMethod.MinLevel, KochMethod.MaxLevel(AlphabetMode.RussianAndLatin));
        profile.CustomSymbols = new string(MorseAlphabet.FilterSupportedSymbols(profile.CustomSymbols ?? string.Empty).ToArray());
    }
}
