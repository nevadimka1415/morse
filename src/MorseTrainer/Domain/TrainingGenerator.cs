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
