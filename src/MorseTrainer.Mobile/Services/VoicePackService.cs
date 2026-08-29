using MorseTrainer.Domain;

namespace MorseTrainer.Mobile.Services;

public sealed class VoicePackService
{
    public async Task<string?> GetVoiceFileAsync(char symbol, CancellationToken cancellationToken = default)
    {
        if (!MorseAlphabet.TryGetCode(symbol, out var code))
        {
            return null;
        }

        var fileName = $"code_{string.Concat(code.Select(item => item == '.' ? '0' : '1'))}.m4a";
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
