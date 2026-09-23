using MorseTrainer.Domain;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Services;

public sealed class VoicePackService
{
    private static readonly string[] CustomExtensions = { ".m4a", ".mp3", ".wav" };

    /// <summary>Голос символа: свой файл из папки voice (m4a, mp3 или wav), иначе встроенный из пакета приложения.</summary>
    public async Task<string?> GetVoiceFileAsync(char symbol, CancellationToken cancellationToken = default)
    {
        var clipName = VoiceClipCatalog.GetClipName(symbol);
        if (clipName is null)
        {
            return null;
        }

        var custom = CustomVoice.Find(MobilePaths.VoiceDirectory, symbol, CustomExtensions);
        if (custom is not null)
        {
            return custom;
        }

        var fileName = $"{clipName}.m4a";
        var target = Path.Combine(FileSystem.CacheDirectory, fileName);
        if (File.Exists(target))
        {
            return target;
        }

        try
        {
            await using var source = await FileSystem.OpenAppPackageFileAsync($"voice/{fileName}");
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination, cancellationToken);
            return target;
        }
        catch
        {
            return null;
        }
    }
}
