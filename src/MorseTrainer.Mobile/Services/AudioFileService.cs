using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Services;

public static class AudioFileService
{
    public static async Task<string> SaveClipAsync(AudioClip clip, string fileName, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, clip.WavBytes, cancellationToken);
        return path;
    }
}
