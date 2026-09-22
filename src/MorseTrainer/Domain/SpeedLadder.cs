using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>
/// Лестница скорости: два задания подряд на текущей скорости с точностью 90 % и выше — скорость +5 зн/мин,
/// задание с точностью ниже 70 % — сразу −5. Чистая функция от истории, одинаковая для Windows и телефона.
/// </summary>
public static class SpeedLadder
{
    public const int Step = 5;
    public const int RequiredStreak = 2;
    public const double RaiseThresholdPercent = 90;
    public const double LowerThresholdPercent = 70;
    public const int MinSpeed = 20;
    public const int MaxSpeed = 300;

    /// <summary>Новая скорость по истории (последняя запись — только что проверенная). Совпадает с текущей, если менять не нужно.</summary>
    public static int Next(IReadOnlyList<TrainingRecord> history, int currentSpeed, string? profileName = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        var recent = history
            .Where(record => profileName is null || string.Equals(record.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.CompletedAt)
            .Take(RequiredStreak)
            .ToList();
        if (recent.Count == 0)
        {
            return currentSpeed;
        }

        if (recent[0].AccuracyPercent < LowerThresholdPercent)
        {
            return Math.Max(MinSpeed, currentSpeed - Step);
        }

        // Повышение только после двух заданий именно на текущей скорости: сразу после подъёма серия начинается заново
        var streak = recent.Count >= RequiredStreak
                     && recent.All(record => record.AccuracyPercent >= RaiseThresholdPercent && record.CharactersPerMinute == currentSpeed);
        return streak ? Math.Min(MaxSpeed, currentSpeed + Step) : currentSpeed;
    }

    /// <summary>Строка для экрана: «Авто-скорость: 60 → 65 знаков/мин» или пусто, если скорость не изменилась.</summary>
    public static string Describe(int from, int to)
    {
        if (from == to)
        {
            return string.Empty;
        }

        return to > from
            ? Texts.F("Авто-скорость: {0} → {1} знаков/мин, два задания подряд от 90 %", from, to)
            : Texts.F("Авто-скорость: {0} → {1} знаков/мин, точность ниже 70 %", from, to);
    }
}
