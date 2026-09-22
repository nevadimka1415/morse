using MorseTrainer.Localization;

namespace MorseTrainer.Domain;

/// <summary>
/// Разбор качества ручной передачи: отношение тире к точке (норма 3:1), разброс длительностей,
/// паузы внутри символа (норма 1 точка), между символами (3) и группами (7) и подсказки по ним.
/// </summary>
public sealed record KeyerAnalysis(
    int DotCount,
    int DashCount,
    double AverageDotMs,
    double AverageDashMs,
    double DashDotRatio,
    double DotSpreadPercent,
    double DashSpreadPercent,
    double AverageElementGapUnits,
    double AverageSymbolGapUnits,
    double AverageGroupGapUnits,
    IReadOnlyList<string> Hints)
{
    public const double IdealRatio = 3;
    public const int MinDots = 3;
    public const int MinDashes = 2;

    public bool HasEnoughData => DotCount >= MinDots && DashCount >= MinDashes;

    public static KeyerAnalysis From(
        double unitMilliseconds,
        IReadOnlyList<double> dots,
        IReadOnlyList<double> dashes,
        IReadOnlyList<double> elementGaps,
        IReadOnlyList<double> symbolGaps,
        IReadOnlyList<double> groupGaps)
    {
        ArgumentNullException.ThrowIfNull(dots);
        ArgumentNullException.ThrowIfNull(dashes);
        ArgumentNullException.ThrowIfNull(elementGaps);
        ArgumentNullException.ThrowIfNull(symbolGaps);
        ArgumentNullException.ThrowIfNull(groupGaps);
        var averageDot = Average(dots);
        var averageDash = Average(dashes);
        var ratio = averageDot > 0 && averageDash > 0 ? averageDash / averageDot : 0;
        var dotSpread = Spread(dots);
        var dashSpread = Spread(dashes);
        var elementUnits = Average(elementGaps) / unitMilliseconds;
        var symbolUnits = Average(symbolGaps) / unitMilliseconds;
        var groupUnits = Average(groupGaps) / unitMilliseconds;

        var hints = new List<string>();
        if (dots.Count < MinDots || dashes.Count < MinDashes)
        {
            hints.Add(Texts.T("Мало данных: передайте хотя бы несколько символов с точками и тире."));
        }
        else
        {
            if (ratio < 2.5)
            {
                hints.Add(Texts.F("Тире коротковаты: {0:0.0}:1 вместо 3:1 — держите ключ дольше.", ratio));
            }
            else if (ratio > 3.6)
            {
                hints.Add(Texts.F("Тире затянуты: {0:0.0}:1 вместо 3:1.", ratio));
            }

            if (dotSpread > 30)
            {
                hints.Add(Texts.F("Точки неровные: разброс {0:0}%.", dotSpread));
            }

            if (dashSpread > 30)
            {
                hints.Add(Texts.F("Тире неровные: разброс {0:0}%.", dashSpread));
            }

            if (elementGaps.Count >= 2 && elementUnits > 1.8)
            {
                hints.Add(Texts.F("Паузы внутри символа затянуты: {0:0.0} точки вместо 1 — символ может распасться на два.", elementUnits));
            }
            else if (elementGaps.Count >= 2 && elementUnits < 0.6)
            {
                hints.Add(Texts.F("Паузы внутри символа слишком короткие: {0:0.0} точки вместо 1.", elementUnits));
            }

            if (symbolGaps.Count >= 2 && symbolUnits > 5)
            {
                hints.Add(Texts.F("Паузы между символами затянуты: {0:0.0} точки вместо 3.", symbolUnits));
            }

            if (groupGaps.Count >= 1 && groupUnits > 12)
            {
                hints.Add(Texts.F("Паузы между группами затянуты: {0:0.0} точек вместо 7.", groupUnits));
            }

            if (hints.Count == 0)
            {
                hints.Add(Texts.F("Ритм ровный: тире {0:0.0}:1, разброс точек {1:0}%, паузы между символами {2:0.0} точки.", ratio, dotSpread, symbolUnits));
            }
        }

        return new KeyerAnalysis(dots.Count, dashes.Count, averageDot, averageDash, ratio, dotSpread, dashSpread,
            elementUnits, symbolUnits, groupUnits, hints);
    }

    /// <summary>Заголовок со статистикой и подсказки, по строке на каждую.</summary>
    public string Describe()
    {
        var header = Texts.F("Точек {0}, тире {1} · тире/точка {2:0.0}:1 · разброс точек {3:0}%, тире {4:0}%",
            DotCount, DashCount, DashDotRatio, DotSpreadPercent, DashSpreadPercent);
        return header + "\n" + string.Join("\n", Hints);
    }

    private static double Average(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Average();

    /// <summary>Разброс в процентах от среднего (среднеквадратичное отклонение).</summary>
    private static double Spread(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var average = values.Average();
        if (average <= 0)
        {
            return 0;
        }

        var variance = values.Sum(value => (value - average) * (value - average)) / values.Count;
        return Math.Sqrt(variance) / average * 100;
    }
}
