namespace MorseTrainer.Domain;

/// <summary>
/// Метод Коха: символы вводятся по одному в фиксированном порядке, всегда на рабочей скорости.
/// Новый символ добавляют, когда точность приёма держится на 90% и выше.
/// </summary>
public static class KochMethod
{
    public const int MinLevel = 2;
    public const double AdvanceThresholdPercent = 90;

    // Порядок LCWO без знаков препинания; цифры на своих местах
    private const string LatinOrder = "KMRSUAPTLOWINJEF0YVG5Q9ZH38B427C1D6X";

    // Русский порядок: те же коды, что в латинском ряду, затем буквы без латинских пар. Ё (код как у Е) не участвует
    private const string RussianOrder = "КМРСУАПТЛОВИНЙЕФ0ЫЖГ5Щ9ЗХ38Б427Ц1Д6ЬЧШЮЯЭЪ";

    private const string LatinLettersOnly = "KMRSUAPTLOWINJEFYVGQZHBCDX";

    public static IReadOnlyList<char> Sequence(AlphabetMode alphabet)
    {
        return alphabet switch
        {
            AlphabetMode.Latin => LatinOrder.ToCharArray(),
            AlphabetMode.RussianAndLatin => (RussianOrder + LatinLettersOnly).ToCharArray(),
            _ => RussianOrder.ToCharArray()
        };
    }

    public static int MaxLevel(AlphabetMode alphabet) => Sequence(alphabet).Count;

    public static int ClampLevel(AlphabetMode alphabet, int level) => Math.Clamp(level, MinLevel, MaxLevel(alphabet));

    /// <summary>Первые level символов ряда.</summary>
    public static IReadOnlyList<char> Pool(AlphabetMode alphabet, int level)
    {
        return Sequence(alphabet).Take(ClampLevel(alphabet, level)).ToArray();
    }

    /// <summary>Символ, который добавится на следующем уровне, или null на последнем уровне.</summary>
    public static char? NextSymbol(AlphabetMode alphabet, int level)
    {
        var sequence = Sequence(alphabet);
        var current = ClampLevel(alphabet, level);
        return current < sequence.Count ? sequence[current] : null;
    }

    public static string Describe(AlphabetMode alphabet, int level)
    {
        var current = ClampLevel(alphabet, level);
        var next = NextSymbol(alphabet, level);
        var text = $"Уровень {current} из {MaxLevel(alphabet)}: {string.Join(' ', Pool(alphabet, current))}";
        return next is null ? text : $"{text} · следующий: {next}";
    }

    public static string Advice(AlphabetMode alphabet, int level, double accuracyPercent)
    {
        var current = ClampLevel(alphabet, level);
        if (accuracyPercent < AdvanceThresholdPercent)
        {
            return $"Метод Коха: точность {accuracyPercent:0.#}%, повторяйте уровень {current}, пока не будет {AdvanceThresholdPercent:0}% и выше.";
        }

        var next = NextSymbol(alphabet, level);
        return next is null
            ? $"Метод Коха: точность {accuracyPercent:0.#}%, это последний уровень, все символы освоены."
            : $"Метод Коха: точность {accuracyPercent:0.#}%, можно перейти на уровень {current + 1} (добавится {next}).";
    }
}
