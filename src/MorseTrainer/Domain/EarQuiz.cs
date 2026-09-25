using System.Security.Cryptography;

namespace MorseTrainer.Domain;

/// <summary>Вопрос проверки на слух: прозвучавший символ и варианты ответа (среди них он сам).</summary>
public sealed record EarQuizQuestion(LearningSymbolItem Target, IReadOnlyList<LearningSymbolItem> Answers);

/// <summary>
/// Проверка на слух (Windows и телефон): звучит символ, из четырёх похожих на слух вариантов нужно выбрать прозвучавший.
/// Скорость сигнала своя, не из настроек тренировки; после ответа следующий символ может звучать сам.
/// Символы, которые путали, звучат чаще (QuizMisses в настройках).
/// </summary>
public static class EarQuiz
{
    public const int MinSpeed = 20;
    public const int MaxSpeed = 200;
    public const int SpeedStep = 5;
    public const int DefaultSpeed = 45;
    public const int AnswerCount = 4;

    /// <summary>Скорость в знаках/мин с шагом 5 в пределах 20–200.</summary>
    public static int ClampSpeed(int speed)
    {
        var rounded = (int)Math.Round(speed / (double)SpeedStep, MidpointRounding.AwayFromZero) * SpeedStep;
        return Math.Clamp(rounded, MinSpeed, MaxSpeed);
    }

    /// <summary>Больше этого числа ошибки по символу не копятся: вес не растёт бесконечно.</summary>
    public const int MaxMisses = 5;

    /// <summary>Пауза перед следующим символом: после ошибки дольше — успеть увидеть верный ответ и напев.</summary>
    public static TimeSpan NextDelay(bool correct) => correct ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2.2);

    /// <summary>
    /// Новый вопрос из набора. Варианты ответа — похожие на слух: ближайшие по коду к прозвучавшему (отличие в один-два
    /// элемента, например к И «··» — Е, А, Н, С), среди одинаково близких — случайные. Варианты с тем же кодом (А и A)
    /// не показываются — их на слух не различить; previous — прошлый символ: подряд один и тот же не звучит.
    /// </summary>
    public static EarQuizQuestion Next(IReadOnlyList<LearningSymbolItem> pool, char? previous = null, Func<int, int>? random = null,
        IReadOnlyDictionary<string, int>? misses = null)
    {
        if (pool.Count == 0)
        {
            throw new ArgumentException("Pool is empty.", nameof(pool));
        }

        random ??= RandomNumberGenerator.GetInt32;
        var candidates = pool.Count > 1 && previous is { } last ? pool.Where(item => item.Symbol != last).ToArray() : pool.ToArray();
        var target = PickWeighted(candidates, misses, random);
        var answers = pool.Where(item => item.Symbol != target.Symbol && item.Code != target.Code)
            .GroupBy(item => item.Code)
            .Select(group => group.First())
            .Select(item => (Item: item, Tie: random(int.MaxValue)))
            .OrderBy(x => ProblemDrill.EditDistance(target.Code, x.Item.Code))
            .ThenBy(x => Math.Abs(target.Code.Length - x.Item.Code.Length))
            .ThenBy(x => x.Tie)
            .Select(x => x.Item)
            .Take(AnswerCount - 1)
            .Append(target)
            .OrderBy(_ => random(int.MaxValue))
            .ToArray();
        return new EarQuizQuestion(target, answers);
    }

    /// <summary>
    /// Вес символа: 1 + 2 за каждую ошибку (не больше MaxMisses). Символ, который путали трижды, звучит в 7 раз чаще
    /// остальных — но и остальные звучат: без них не проверить, что их не путают.
    /// </summary>
    public static int Weight(IReadOnlyDictionary<string, int>? misses, char symbol) =>
        1 + 2 * Math.Clamp(misses is not null && misses.TryGetValue(symbol.ToString(), out var count) ? count : 0, 0, MaxMisses);

    /// <summary>Учёт ответа: ошибка — +1 прозвучавшему символу, верный ответ — −1 (на нуле символ убирается).</summary>
    public static void Record(IDictionary<string, int> misses, char symbol, bool correct)
    {
        var key = symbol.ToString();
        var count = misses.TryGetValue(key, out var value) ? value : 0;
        count = correct ? count - 1 : Math.Min(MaxMisses, count + 1);
        if (count > 0)
        {
            misses[key] = count;
        }
        else
        {
            misses.Remove(key);
        }
    }

    /// <summary>Какие символы сейчас звучат чаще — по убыванию ошибок, для подсказки под счётом.</summary>
    public static IReadOnlyList<char> Frequent(IReadOnlyDictionary<string, int>? misses, int count = 5) =>
        misses is null
            ? Array.Empty<char>()
            : misses.Where(pair => pair.Value > 0 && pair.Key.Length == 1)
                .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(count).Select(pair => pair.Key[0]).ToArray();

    private static LearningSymbolItem PickWeighted(IReadOnlyList<LearningSymbolItem> candidates, IReadOnlyDictionary<string, int>? misses,
        Func<int, int> random)
    {
        var total = candidates.Sum(item => Weight(misses, item.Symbol));
        var roll = random(total);
        foreach (var item in candidates)
        {
            roll -= Weight(misses, item.Symbol);
            if (roll < 0)
            {
                return item;
            }
        }

        return candidates[^1];
    }
}
