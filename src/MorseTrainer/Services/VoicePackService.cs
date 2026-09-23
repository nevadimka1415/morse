using System.IO;
using System.Reflection;
using MorseTrainer.Domain;

namespace MorseTrainer.Services;

public static class VoicePackService
{
    private static readonly string[] CustomExtensions = { ".wav" };

    /// <summary>Голос символа: свой файл из папки voice (только WAV — его играет SoundPlayer), иначе встроенный.</summary>
    public static MemoryStream? Open(char symbol)
    {
        var clipName = VoiceClipCatalog.GetClipName(symbol);
        if (clipName is null)
        {
            return null;
        }

        var custom = CustomVoice.Find(AppPaths.VoiceDirectory, symbol, CustomExtensions);
        if (custom is not null)
        {
            try
            {
                return new MemoryStream(File.ReadAllBytes(custom), writable: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Файл занят или недоступен — звучит встроенный напев
            }
        }

        var suffix = $"Assets.Voice.{clipName}.wav";
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return null;
        }

        using var resource = assembly.GetManifestResourceStream(resourceName);
        if (resource is null)
        {
            return null;
        }

        var result = new MemoryStream();
        resource.CopyTo(result);
        result.Position = 0;
        return result;
    }
}
