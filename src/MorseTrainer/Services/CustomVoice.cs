using System.IO;
using System.Text.RegularExpressions;
using MorseTrainer.Domain;
using MorseTrainer.Localization;

namespace MorseTrainer.Services;

/// <summary>
/// Свой голосовой пакет: файлы code_XXXX.wav (на телефоне также .m4a и .mp3) в папке voice рядом с данными
/// перекрывают встроенные напевы. Имя — код символа, где точка — 0, тире — 1: «А» (.-) → code_01.wav.
/// </summary>
public static class CustomVoice
{
    public const string FolderName = "voice";

    private static readonly Regex ClipFileName = new(@"^code_[01]{1,7}\.(wav|m4a|mp3)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsClipFileName(string? fileName) => fileName is not null && ClipFileName.IsMatch(fileName);

    /// <summary>Путь к своему клипу символа с одним из расширений (по порядку) или null.</summary>
    public static string? Find(string directory, char symbol, IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var clipName = VoiceClipCatalog.GetClipName(symbol);
        if (clipName is null || string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        foreach (var extension in extensions)
        {
            var path = Path.Combine(directory, clipName + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static int Count(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Count(path => IsClipFileName(Path.GetFileName(path))) : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static string Describe(int count) => count == 0
        ? Texts.T("Свой голос не добавлен: звучат встроенные напевы.")
        : Texts.F("Свой голос: файлов {0}. Они заменяют встроенные напевы для своих символов.", count);

    /// <summary>Имя файла для символа, чтобы подсказать пользователю: «А» → code_01.wav.</summary>
    public static string ExampleFileName(char symbol, string extension = ".wav") => (VoiceClipCatalog.GetClipName(symbol) ?? "code_01") + extension;
}
