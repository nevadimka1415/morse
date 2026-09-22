namespace MorseTrainer.Domain;

/// <summary>Ежедневное напоминание: время дня в минутах от полуночи и ближайший момент срабатывания.</summary>
public static class ReminderSchedule
{
    public const int DefaultMinutes = 19 * 60;
    public const int MinutesPerDay = 24 * 60;

    public static int ClampMinutes(int minutes) => ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;

    public static TimeSpan ToTime(int minutes) => TimeSpan.FromMinutes(ClampMinutes(minutes));

    public static int ToMinutes(TimeSpan time) => ClampMinutes((int)Math.Round(time.TotalMinutes));

    /// <summary>Ближайшее срабатывание: сегодня, если время ещё не прошло, иначе завтра.</summary>
    public static DateTime NextOccurrence(DateTime now, TimeSpan time)
    {
        var candidate = now.Date + time;
        return candidate > now ? candidate : candidate.AddDays(1);
    }
}
