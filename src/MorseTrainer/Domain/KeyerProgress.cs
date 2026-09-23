namespace MorseTrainer.Domain;

/// <summary>
/// Подсветка задания в «Передаче» по мере приёма: сколько символов задания (вместе с пробелами между словами)
/// уже передано верно подряд с начала. Символы с одинаковым кодом (А/A) считаются совпадением.
/// </summary>
public static class KeyerProgress
{
    public static int MatchedLength(string? target, string? received)
    {
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(received))
        {
            return 0;
        }

        var sent = received.Where(symbol => !char.IsWhiteSpace(symbol)).ToArray();
        var next = 0;
        var matchedEnd = 0;
        for (var index = 0; index < target.Length && next < sent.Length; index++)
        {
            if (char.IsWhiteSpace(target[index]))
            {
                continue;
            }

            if (!TrainingEvaluator.Matches(target[index], sent[next]))
            {
                break;
            }

            next++;
            matchedEnd = index + 1;
        }

        return matchedEnd;
    }
}
