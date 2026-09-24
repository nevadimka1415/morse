namespace MorseTrainer.Domain;

public sealed record CharacterMistake(int Position, char Expected, char? Actual);

public sealed record EvaluationResult(
    int CorrectCount,
    int TotalCount,
    double AccuracyPercent,
    IReadOnlyList<CharacterMistake> Mistakes)
{
    public bool IsPerfect => CorrectCount == TotalCount && Mistakes.Count == 0;
}

public static class TrainingEvaluator
{
    public static EvaluationResult Evaluate(string expected, string actual)
    {
        var normalizedExpected = Normalize(expected);
        var normalizedActual = Normalize(actual);
        var mistakes = new List<CharacterMistake>();
        var correct = 0;

        for (var index = 0; index < normalizedExpected.Length; index++)
        {
            var actualSymbol = index < normalizedActual.Length ? normalizedActual[index] : (char?)null;
            if (actualSymbol is not null && Matches(normalizedExpected[index], actualSymbol.Value))
            {
                correct++;
            }
            else
            {
                mistakes.Add(new CharacterMistake(index + 1, normalizedExpected[index], actualSymbol));
            }
        }

        if (normalizedActual.Length > normalizedExpected.Length)
        {
            for (var index = normalizedExpected.Length; index < normalizedActual.Length; index++)
            {
                mistakes.Add(new CharacterMistake(index + 1, '\u2205', normalizedActual[index]));
            }
        }

        var accuracy = normalizedExpected.Length == 0
            ? 0
            : correct * 100d / normalizedExpected.Length;

        return new EvaluationResult(correct, normalizedExpected.Length, accuracy, mistakes);
    }

    /// <summary>
    /// Символы с одинаковым кодом Морзе (А/A, Р/R) на слух неразличимы,
    /// поэтому ответ другим алфавитом засчитывается как верный.
    /// </summary>
    public static bool Matches(char expected, char actual)
    {
        if (expected == actual)
        {
            return true;
        }

        return MorseAlphabet.TryGetCode(expected, out var expectedCode)
               && MorseAlphabet.TryGetCode(actual, out var actualCode)
               && expectedCode == actualCode;
    }

    /// <summary>Верхний регистр без пробелов; набранная по привычке Ё считается Е — в азбуке это одна буква.</summary>
    public static string Normalize(string value)
    {
        return new string(value
            .ToUpperInvariant()
            .Where(symbol => !char.IsWhiteSpace(symbol))
            .Select(symbol => symbol == 'Ё' ? 'Е' : symbol)
            .ToArray());
    }
}
