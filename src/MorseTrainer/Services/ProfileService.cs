using System.IO;
using System.Text.Json;
using MorseTrainer.Domain;
using MorseTrainer.Models;

namespace MorseTrainer.Services;

public sealed class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _profilesPath;

    public ProfileService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _profilesPath = Path.Combine(appData, "MorseTrainer", "profiles.json");
    }

    public IReadOnlyList<TrainingProfile> Load()
    {
        try
        {
            if (!File.Exists(_profilesPath))
            {
                return Array.Empty<TrainingProfile>();
            }

            var profiles = JsonSerializer.Deserialize<List<TrainingProfile>>(
                File.ReadAllText(_profilesPath), JsonOptions) ?? new List<TrainingProfile>();
            return profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
                .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<TrainingProfile>();
        }
    }

    public IReadOnlyList<TrainingProfile> Save(TrainingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Name = TrainingProfile.NormalizeName(profile.Name);
        var profiles = Load().ToList();
        var existingIndex = profiles.FindIndex(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            profiles[existingIndex] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        Write(profiles);
        return profiles.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public IReadOnlyList<TrainingProfile> Delete(string name)
    {
        var profiles = Load()
            .Where(profile => !string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Write(profiles);
        return profiles;
    }

    public IReadOnlyList<TrainingProfile> Import(IEnumerable<TrainingProfile> imported)
    {
        var merged = ProfileTransfer.Merge(Load(), imported);
        Write(merged);
        return merged;
    }

    private void Write(IEnumerable<TrainingProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_profilesPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(_profilesPath, JsonSerializer.Serialize(profiles, JsonOptions));
    }
}
