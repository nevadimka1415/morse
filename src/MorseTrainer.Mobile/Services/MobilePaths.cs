using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Services;

/// <summary>Файлы данных телефона в папке приложения (история, отчёт о сбое).</summary>
public static class MobilePaths
{
    public static string HistoryFile => Path.Combine(FileSystem.AppDataDirectory, "history.json");

    public static string CrashLogFile => Path.Combine(FileSystem.AppDataDirectory, CrashReport.FileName);
}
