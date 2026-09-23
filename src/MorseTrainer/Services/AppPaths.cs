using System.IO;

namespace MorseTrainer.Services;

/// <summary>
/// Папка данных Windows-приложения: %LOCALAPPDATA%\MorseTrainer. Переменная окружения
/// MORSETRAINER_DATA_DIR переопределяет её, чтобы дымовой UI-тест не трогал настройки и историю пользователя.
/// </summary>
public static class AppPaths
{
    public const string DataDirectoryVariable = "MORSETRAINER_DATA_DIR";

    public static string DataDirectory { get; } = Resolve();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");

    public static string HistoryFile => Path.Combine(DataDirectory, "history.json");

    public static string CrashLogFile => Path.Combine(DataDirectory, "crash.log");

    public static string ChantsFile => Path.Combine(DataDirectory, "chants.json");

    public static string VoiceDirectory => Path.Combine(DataDirectory, CustomVoice.FolderName);

    private static string Resolve()
    {
        var custom = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorseTrainer");
    }
}
