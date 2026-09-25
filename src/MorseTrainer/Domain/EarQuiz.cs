using System.Security.Cryptography;

namespace MorseTrainer.Domain;

/// <summary>Вопрос проверки на слух: прозвучавший символ и варианты ответа (среди них он сам).</summary>
public sealed record EarQuizQuestion(LearningSymbolItem Target, IReadOnlyList<LearningSymbolItem> Answers);

/// <summary>
/// Проверка на слух (Windows и телефон): звучит символ, из четырёх похожих на слух вариантов нужно выбрать прозвучавший.
/// Скорость сигнала своя, не из настроек тренировки; после ответа следующий символ может звучать сам.
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

    /// <summary>Пауза перед следующим символом: после ошибки дольше — успеть увидеть верный ответ и напев.</summary>
    public static TimeSpan NextDelay(bool correct) => correct ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(2.2);

    /// <summary>
    /// Новый вопрос из набора. Варианты ответа — похожие на слух: ближайшие по коду к прозвучавшему (отличие в один-два
    /// элемента, например к И «··» — Е, А, Н, С), среди одинаково близких — случайные. Варианты с тем же кодом (А и A)
    /// не показываются — их на слух не различить; previous — прошлый символ: подряд один и тот же не звучит.
    /// </summary>
    public static EarQuizQuestion Next(IReadOnlyList<LearningSymbolItem> pool, char? previous = null, Func<int, int>? random = null)
    {
        if (pool.Count == 0)
        {
            throw new ArgumentException("Pool is empty.", nameof(pool));
        }

        random ??= RandomNumberGenerator.GetInt32;
        var candidates = pool.Count > 1 && previous is { } last ? pool.Where(item => item.Symbol != last).ToArray() : pool.ToArray();
        var target = candidates[random(candidates.Length)];
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
}
