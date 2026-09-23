using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>Состав упражнения «Повторить сложные символы».</summary>
public sealed record DrillPlan(IReadOnlyList<char> Problems, IReadOnlyList<char> Similar)
{
    public bool IsEmpty => Problems.Count == 0;

    /// <summary>Все символы упражнения: сначала сложные, затем похожие на них по коду.</summary>
    public IReadOnlyList<char> Pool => Problems.Concat(Similar).ToArray();

    public string Describe() => Similar.Count == 0
        ? Texts.F("Повтор сложных символов: {0}", string.Join(' ', Problems))
        : Texts.F("Повтор сложных символов: {0} · похожие по коду: {1}", string.Join(' ', Problems), string.Join(' ', Similar));
}

/// <summary>
/// Упражнение на ошибки одной кнопкой: символы, в которых чаще всего ошибались в последних заданиях,
/// плюс символы с похожим кодом (отличие в один-два элемента) — именно их ухо путает между собой.
/// </summary>
public static class ProblemDrill
{
    public const int TopProblems = 8;
    public const int SimilarPerSymbol = 2;
    public const int RecentRecords = 100;

    public static DrillPlan Build(IReadOnlyList<TrainingRecord> history, int top = TopProblems, int similarPerSymbol = SimilarPerSymbol)
    {
        ArgumentNullException.ThrowIfNull(history);
        var recent = history.OrderBy(item => item.CompletedAt).TakeLast(RecentRecords).ToArray();
        var problems = TrainingStatistics.ProblemSymbols(recent, top)
            .Select(item => char.ToUpperInvariant(item.Symbol))
            .Where(symbol => MorseAlphabet.TryGetCode(symbol, out _))
            .DistinctBy(CodeOf)
            .ToArray();
        if (problems.Length == 0)
        {
            return new DrillPlan(Array.Empty<char>(), Array.Empty<char>());
        }

        // Коды уже взятых символов: А и латинская A звучат одинаково, второй такой символ ничего не добавляет
        var takenCodes = new HashSet<string>(problems.Select(CodeOf));
        var similar = new List<char>();
        foreach (var symbol in problems)
        {
            var added = 0;
            foreach (var candidate in SimilarSymbols(symbol, int.MaxValue))
            {
                if (added >= similarPerSymbol)
                {
                    break;
                }

                if (takenCodes.Add(CodeOf(candidate)))
                {
                    similar.Add(candidate);
                    added++;
                }
            }
        }

        return new DrillPlan(problems, similar);
    }

    /// <summary>
    /// Символы того же набора (русские буквы, латинские, цифры, знаки), чьи коды ближе всего к коду symbol:
    /// сначала отличие в один элемент (замена, лишний или пропущенный), затем в два; одинаковой длины — раньше.
    /// </summary>
    public static IReadOnlyList<char> SimilarSymbols(char symbol, int count = SimilarPerSymbol)
    {
        symbol = char.ToUpperInvariant(symbol);
        if (!MorseAlphabet.TryGetCode(symbol, out var code) || count <= 0)
        {
            return Array.Empty<char>();
        }

        return CandidateMap(symbol)
            .Where(pair => pair.Key != symbol && pair.Value != code)
            .Select((pair, order) => (pair.Key, Distance: EditDistance(code, pair.Value), LengthGap: Math.Abs(code.Length - pair.Value.Length), Order: order))
            .Where(item => item.Distance <= 2)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.LengthGap)
            .ThenBy(item => item.Order)
            .Take(count)
            .Select(item => item.Key)
            .ToArray();
    }

    private static IEnumerable<KeyValuePair<char, string>> CandidateMap(char symbol)
    {
        if (MorseAlphabet.Russian.ContainsKey(symbol))
        {
            return MorseAlphabet.Russian;
        }

        if (MorseAlphabet.Latin.ContainsKey(symbol))
        {
            return MorseAlphabet.Latin;
        }

        if (MorseAlphabet.Digits.ContainsKey(symbol))
        {
            return MorseAlphabet.Digits;
        }

        return MorseAlphabet.Punctuation.Concat(MorseAlphabet.Digits);
    }

    private static string CodeOf(char symbol) => MorseAlphabet.TryGetCode(symbol, out var code) ? code : string.Empty;

    /// <summary>Расстояние Левенштейна между кодами из точек и тире.</summary>
    public static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[right.Length];
    }
}
