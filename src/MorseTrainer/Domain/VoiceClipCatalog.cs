namespace MorseTrainer.Domain;

/// <summary>
/// Имя голосового клипа для символа: точка → 0, тире → 1, «А» (.-) → code_01.
/// Один и тот же набор имён используют Windows (WAV в ресурсах) и телефон (m4a в пакете).
/// </summary>
public static class VoiceClipCatalog
{
    public static string? GetClipName(char symbol)
    {
        if (!MorseAlphabet.TryGetCode(symbol, out var code))
        {
            return null;
        }

        return "code_" + string.Concat(code.Select(item => item == '.' ? '0' : '1'));
    }

    /// <summary>Все имена клипов, нужные для букв и цифр (без знаков препинания).</summary>
    public static IReadOnlyCollection<string> RequiredClipNames()
    {
        return MorseAlphabet.Russian.Keys
            .Concat(MorseAlphabet.Latin.Keys)
            .Concat(MorseAlphabet.Digits.Keys)
            .Select(GetClipName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
