namespace MorseTrainer.Domain;

/// <summary>Близость кодов Морзе друг к другу.</summary>
public static class CodeDistance
{
    /// <summary>
    /// Расстояние Левенштейна между кодами из точек и тире: сколько элементов нужно заменить, добавить или убрать.
    /// Нужно упражнению «На слух», чтобы подбирать варианты ответа с похожим кодом.
    /// </summary>
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
