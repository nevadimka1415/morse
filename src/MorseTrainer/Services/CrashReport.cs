using System.IO;
using System.Text;

namespace MorseTrainer.Services;

/// <summary>
/// Отчёт о сбое: один текстовый файл crash.log одинакового формата на Windows и телефоне.
/// Windows пишет его в %LOCALAPPDATA%\MorseTrainer, телефон — в папку данных приложения.
/// </summary>
public static class CrashReport
{
    public const string FileName = "crash.log";

    public static string Format(string source, Exception? exception, string version, string platform)
    {
        return new StringBuilder()
            .AppendLine("Morse Trainer crash report")
            .AppendLine($"Time: {DateTime.Now:O}")
            .AppendLine($"Source: {source}")
            .AppendLine($"Version: {version}")
            .AppendLine($"Platform: {platform}")
            .AppendLine($".NET: {Environment.Version}")
            .AppendLine()
            .AppendLine(exception?.ToString() ?? "Unknown exception")
            .ToString();
    }

    /// <summary>Записывает отчёт; любая ошибка записи глотается — отчёт о сбое не должен вызывать новый сбой.</summary>
    public static bool Write(string path, string source, Exception? exception, string version, string platform)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, Format(source, exception, version, platform), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>Время последнего сбоя по файлу или null, если отчёта нет.</summary>
    public static DateTime? LastCrashAt(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTime(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Нечего удалять или нет прав
        }
    }
}
