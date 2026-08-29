using System.Security.Cryptography;

namespace MorseTrainer.Domain;

public static class TrainingGenerator
{
    public const int GroupSize = 5;

    public static string Generate(IReadOnlyList<char> pool, int groupCount)
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

        var groups = new string[groupCount];
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var symbols = new char[GroupSize];
            for (var symbolIndex = 0; symbolIndex < GroupSize; symbolIndex++)
            {
                symbols[symbolIndex] = pool[RandomNumberGenerator.GetInt32(pool.Count)];
            }

            groups[groupIndex] = new string(symbols);
        }

        return string.Join(' ', groups);
    }
}
