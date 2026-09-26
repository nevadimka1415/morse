using System.Security.Cryptography;

namespace MorseTrainer.Domain;

public static class TrainingGenerator
{
    public const int GroupSize = 5;

    /// <summary>
    /// Случайные группы из pool. Символы из emphasized встречаются в emphasisWeight раз чаще остальных:
    /// так чаще звучит новый символ метода Коха и символы, в которых были ошибки.
    /// </summary>
    public static string Generate(
        IReadOnlyList<char> pool,
        int groupCount,
        IReadOnlyCollection<char>? emphasized = null,
        int emphasisWeight = 3)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (pool.Count == 0)
        {
            throw new ArgumentException("The symbol pool must not be empty.", nameof(pool));
        }

        if (groupCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCount), "Group count must be between 1 and 100.");
        }

        var weighted = BuildWeightedPool(pool, emphasized, Math.Max(1, emphasisWeight));
        var groups = new string[groupCount];
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var symbols = new char[GroupSize];
            for (var symbolIndex = 0; symbolIndex < GroupSize; symbolIndex++)
            {
                symbols[symbolIndex] = weighted[RandomNumberGenerator.GetInt32(weighted.Count)];
            }

            groups[groupIndex] = new string(symbols);
        }

        return string.Join(' ', groups);
    }

    /// <summary>
    /// Задание по режиму: слова, позывные и Q-код берутся из словарей (groupCount — число слов),
    /// остальные режимы — случайные группы по пять символов из pool.
    /// </summary>
    public static string GenerateTask(
        ContentMode content,
        AlphabetMode alphabet,
        IReadOnlyList<char> pool,
        int groupCount,
        IReadOnlyCollection<char>? emphasized = null)
    {
        return content switch
        {
            ContentMode.Words => GenerateWords(WordLists.Words(alphabet), groupCount, emphasized),
            ContentMode.QCodes => GenerateWords(WordLists.QCodes, groupCount, emphasized),
            ContentMode.Callsigns => GenerateCallsigns(groupCount),
            ContentMode.RadioExchange => GenerateExchange(groupCount),
            _ => Generate(pool, groupCount, emphasized)
        };
    }

    /// <summary>Случайные слова из словаря; слова с символами из emphasized встречаются чаще.</summary>
    public static string GenerateWords(
        IReadOnlyList<string> words,
        int count,
        IReadOnlyCollection<char>? emphasized = null,
        int emphasisWeight = 3)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Count == 0)
        {
            throw new ArgumentException("The word list must not be empty.", nameof(words));
        }

        if (count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Word count must be between 1 and 100.");
        }

        var emphasizedSet = new HashSet<char>((emphasized ?? Array.Empty<char>()).Select(char.ToUpperInvariant));
        var weighted = new List<string>(words.Count * 2);
        foreach (var word in words)
        {
            var copies = emphasizedSet.Count > 0 && word.Any(symbol => emphasizedSet.Contains(char.ToUpperInvariant(symbol)))
                ? Math.Max(1, emphasisWeight)
                : 1;
            for (var index = 0; index < copies; index++)
            {
                weighted.Add(word);
            }
        }

        var picks = new string[count];
        for (var index = 0; index < count; index++)
        {
            picks[index] = weighted[RandomNumberGenerator.GetInt32(weighted.Count)];
        }

        return string.Join(' ', picks);
    }

    /// <summary>
    /// Радиообмен: связи подряд (CQ, ответ, рапорт, 73), wordCount — сколько слов принять; связь обрывается на границе
    /// слова. Для целой связи — около 45 слов. Задание короче половины связи начинается с одного из естественных мест: вызов,
    /// ответ, рапорт (UR), имя (NAME), ответная передача — а не всегда с «CQ CQ DE».
    /// </summary>
    public static string GenerateExchange(int wordCount)
    {
        if (wordCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(wordCount), "Word count must be between 1 and 100.");
        }

        var first = WordLists.RandomExchange().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var starts = new List<int> { 0 };
        for (var index = 1; index < first.Length; index++)
        {
            if (first[index - 1] is "K" or "KN" || first[index] is "UR" or "NAME")
            {
                starts.Add(index);
            }
        }

        var start = wordCount * 2 >= first.Length ? 0 : starts[RandomNumberGenerator.GetInt32(starts.Count)];
        var words = first.Skip(start).ToList();
        while (words.Count < wordCount)
        {
            words.AddRange(WordLists.RandomExchange().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        return string.Join(' ', words.Take(wordCount));
    }

    public static string GenerateCallsigns(int count)
    {
        if (count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Callsign count must be between 1 and 100.");
        }

        return string.Join(' ', Enumerable.Range(0, count).Select(_ => WordLists.RandomCallsign()));
    }

    private static IReadOnlyList<char> BuildWeightedPool(IReadOnlyList<char> pool, IReadOnlyCollection<char>? emphasized, int weight)
    {
        if (emphasized is null || emphasized.Count == 0 || weight == 1)
        {
            return pool;
        }

        var emphasizedSet = new HashSet<char>(emphasized.Select(char.ToUpperInvariant));
        var result = new List<char>(pool.Count * weight);
        foreach (var symbol in pool)
        {
            var copies = emphasizedSet.Contains(char.ToUpperInvariant(symbol)) ? weight : 1;
            for (var index = 0; index < copies; index++)
            {
                result.Add(symbol);
            }
        }

        return result;
    }
}
