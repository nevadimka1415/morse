using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>Содержимое файла профилей: сами профили и свои напевы (если их передавали).</summary>
public sealed record ProfilePackage(IReadOnlyList<TrainingProfile> Profiles, IReadOnlyDictionary<char, string> Chants);

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

        // Свои напевы едут вместе с профилями; в старых файлах поля нет
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Chants { get; set; }
    }

    public static string Export(IEnumerable<TrainingProfile> profiles, IReadOnlyDictionary<char, string>? chants = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var envelope = new Envelope
        {
            ExportedAt = DateTime.Now,
            Profiles = profiles.ToList(),
            Chants = chants is { Count: > 0 } ? chants.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value) : null
        };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    /// <summary>Профили из текста (свои напевы, если есть, отбрасываются — см. ImportPackage).</summary>
    public static IReadOnlyList<TrainingProfile> Import(string text) => ImportPackage(text).Profiles;

    /// <summary>Принимает конверт Export или просто массив профилей; значения приводятся к допустимым диапазонам, напевы проверяются.</summary>
    public static ProfilePackage ImportPackage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Текст пустой.");
        }

        var trimmed = text.Trim();
        List<TrainingProfile>? profiles;
        Dictionary<string, string>? chants = null;
        try
        {
            if (trimmed.StartsWith('['))
            {
                profiles = JsonSerializer.Deserialize<List<TrainingProfile>>(trimmed, JsonOptions);
            }
            else
            {
                var envelope = JsonSerializer.Deserialize<Envelope>(trimmed, JsonOptions);
                profiles = envelope?.Profiles;
                chants = envelope?.Chants;
            }
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

        return new ProfilePackage(result, ChantBook.Clean(chants));
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
        profile.ContentModeIndex = Math.Clamp(profile.ContentModeIndex, 0, ContentModes.MaxIndex);
        profile.GroupCount = Math.Clamp(profile.GroupCount, 1, 100);
        profile.CharactersPerMinute = Math.Clamp(profile.CharactersPerMinute, 20, 300);
        profile.FrequencyHz = Math.Clamp(profile.FrequencyHz, 300, 1200);
        profile.VolumePercent = Math.Clamp(profile.VolumePercent, 0, 100);
        profile.CharacterGapUnits = Math.Clamp(profile.CharacterGapUnits, 3, 20);
        profile.GroupGapUnits = Math.Clamp(profile.GroupGapUnits, 7, 30);
        profile.StartPauseUnits = Math.Clamp(profile.StartPauseUnits, 7, 60);
        profile.NoisePercent = Math.Clamp(profile.NoisePercent, 0, 100);
        profile.QsbPercent = Math.Clamp(profile.QsbPercent, 0, 100);
        profile.DriftHz = Math.Clamp(profile.DriftHz, 0, MorseTrainer.Services.NoiseProfile.MaxDriftHz);
        profile.KochLevel = Math.Clamp(profile.KochLevel, KochMethod.MinLevel, KochMethod.MaxLevel(AlphabetMode.RussianAndLatin));
        profile.CustomSymbols = new string(MorseAlphabet.FilterSupportedSymbols(profile.CustomSymbols ?? string.Empty).ToArray());
    }
}
