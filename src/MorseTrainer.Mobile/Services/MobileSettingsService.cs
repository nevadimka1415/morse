using System.Text.Json;
using MorseTrainer.Models;

namespace MorseTrainer.Mobile.Services;

public sealed class MobileSettingsService
{
    private const string SettingsKey = "morse-settings-v2";
    private const string ProfilesKey = "morse-profiles-v2";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public event EventHandler? SettingsChanged;

    public AppSettings LoadSettings()
    {
        try
        {
            var json = Preferences.Default.Get(SettingsKey, string.Empty);
            return string.IsNullOrWhiteSpace(json)
                ? new AppSettings()
                : JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        Preferences.Default.Set(SettingsKey, JsonSerializer.Serialize(settings, JsonOptions));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<TrainingProfile> LoadProfiles()
    {
        try
        {
            var json = Preferences.Default.Get(ProfilesKey, string.Empty);
            var profiles = string.IsNullOrWhiteSpace(json)
                ? new List<TrainingProfile>()
                : JsonSerializer.Deserialize<List<TrainingProfile>>(json, JsonOptions) ?? new List<TrainingProfile>();
            if (profiles.Count == 0)
            {
                profiles.Add(TrainingProfile.FromSettings("Основной", LoadSettings()));
                WriteProfiles(profiles);
            }

            return profiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch
        {
            return new[] { TrainingProfile.FromSettings("Основной", new AppSettings()) };
        }
    }

    public IReadOnlyList<TrainingProfile> SaveProfile(string name, AppSettings settings)
    {
        var profile = TrainingProfile.FromSettings(name, settings);
        var profiles = LoadProfiles().ToList();
        var index = profiles.FindIndex(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        settings.ActiveProfileName = profile.Name;
        WriteProfiles(profiles);
        SaveSettings(settings);
        return profiles.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public IReadOnlyList<TrainingProfile> DeleteProfile(string name)
    {
        var profiles = LoadProfiles()
            .Where(profile => !string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (profiles.Count == 0)
        {
            profiles.Add(TrainingProfile.FromSettings("Основной", LoadSettings()));
        }

        WriteProfiles(profiles);
        return profiles;
    }

    private static void WriteProfiles(IEnumerable<TrainingProfile> profiles)
    {
        Preferences.Default.Set(ProfilesKey, JsonSerializer.Serialize(profiles, JsonOptions));
    }
}
