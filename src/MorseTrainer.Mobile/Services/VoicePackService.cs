using MorseTrainer.Domain;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Services;

public sealed class VoicePackService
{
    private static readonly string[] CustomExtensions = { ".m4a", ".mp3", ".wav" };

    // Android и iOS не чистят кэш при обновлении приложения: копии встроенного голоса лежат в папке своей сборки,
    // иначе после обновления звучал бы голос из прошлой версии (так было с 2.6.1 для уже прослушанных букв)
    private static readonly Lazy<string> BuiltInCache = new(PrepareBuiltInCache);

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
        try
        {
            var target = Path.Combine(BuiltInCache.Value, fileName);
            if (File.Exists(target))
            {
                return target;
            }

            // Сначала во временный файл: оборванное копирование не оставит в кэше обрезанный напев
            var partial = target + ".part";
            await using (var source = await FileSystem.OpenAppPackageFileAsync($"voice/{fileName}"))
            await using (var destination = File.Create(partial))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch
        {
            return null;
        }
    }

    private static string PrepareBuiltInCache()
    {
        var root = FileSystem.CacheDirectory;
        var directory = Path.Combine(root, $"voice-{AppInfo.Current.VersionString}-{AppInfo.Current.BuildString}");
        try
        {
            // Копии прошлых версий: папки voice-* и файлы code_*.m4a прямо в кэше (так хранили до 2.6.2)
            foreach (var old in Directory.EnumerateDirectories(root, "voice-*"))
            {
                if (!string.Equals(old, directory, StringComparison.Ordinal))
                {
                    Directory.Delete(old, recursive: true);
                }
            }

            foreach (var old in Directory.EnumerateFiles(root, "code_*.m4a"))
            {
                File.Delete(old);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Не удалось убрать старые копии — не страшно, новые всё равно берутся из папки этой сборки
        }

        Directory.CreateDirectory(directory);
        return directory;
    }
}
