using MorseTrainer.Domain;

namespace MorseTrainer.Mobile.Services;

public sealed class VoicePackService
{
    public async Task<string?> GetVoiceFileAsync(char symbol, CancellationToken cancellationToken = default)
    {
        var clipName = VoiceClipCatalog.GetClipName(symbol);
        if (clipName is null)
        {
            return null;
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
