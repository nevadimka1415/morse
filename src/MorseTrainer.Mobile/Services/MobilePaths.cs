using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Services;

/// <summary>Файлы данных телефона в папке приложения (история, отчёт о сбое).</summary>
public static class MobilePaths
{
    public static string HistoryFile => Path.Combine(FileSystem.AppDataDirectory, "history.json");

    public static string CrashLogFile => Path.Combine(FileSystem.AppDataDirectory, CrashReport.FileName);

    public static string ChantsFile => Path.Combine(FileSystem.AppDataDirectory, "chants.json");

    public static string VoiceDirectory => Path.Combine(FileSystem.AppDataDirectory, CustomVoice.FolderName);
}
