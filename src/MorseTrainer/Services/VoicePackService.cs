using System.IO;
using System.Reflection;
using MorseTrainer.Domain;

namespace MorseTrainer.Services;

public static class VoicePackService
{
    public static MemoryStream? Open(char symbol)
    {
        if (!MorseAlphabet.TryGetCode(symbol, out var code))
        {
            return null;
        }

        var suffix = $"Assets.Voice.code_{string.Concat(code.Select(item => item == '.' ? '0' : '1'))}.wav";
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
