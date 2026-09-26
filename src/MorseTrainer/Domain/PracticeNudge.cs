using System.Globalization;
using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>
/// Напоминание на Windows: плашка при запуске, если давно не занимались, и момент ежедневного уведомления,
/// пока программа открыта (планировщик задач не нужен).
/// </summary>
public static class PracticeNudge
{
    /// <summary>Плашка появляется, если пропущено столько дней и больше.</summary>
    public const int BannerAfterDays = 2;

    /// <summary>Шаг списка времени напоминания в минутах.</summary>
    public const int TimeStepMinutes = 30;

    /// <summary>
    /// Сколько дней прошло с последней тренировки; null — не занимались ни разу. lastPractice — занятие без записи
    /// в истории (задание прослушано и сверено на бумаге, ответ «На слух»).
    /// </summary>
    public static int? DaysSinceLastPractice(IReadOnlyList<TrainingRecord> records, DateOnly today, DateTime? lastPractice = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        DateTime? last = records.Count == 0 ? null : records.Max(item => item.CompletedAt);
        if (lastPractice is { } practice && (last is null || practice > last))
        {
            last = practice;
        }

        return last is null ? null : Math.Max(0, today.DayNumber - DateOnly.FromDateTime(last.Value).DayNumber);
    }

    /// <summary>Занимались ли сегодня: запись в истории или занятие без неё.</summary>
    public static bool PracticedOn(DateOnly day, IReadOnlyList<TrainingRecord> records, DateTime? lastPractice) =>
        (lastPractice is { } practice && DateOnly.FromDateTime(practice) == day) || records.Any(item => DateOnly.FromDateTime(item.CompletedAt) == day);

    /// <summary>Текст плашки «давно не занимались» или null, если показывать нечего.</summary>
    public static string? Banner(IReadOnlyList<TrainingRecord> records, DateOnly today, DateTime? lastPractice = null)
    {
        var days = DaysSinceLastPractice(records, today, lastPractice);
        return days is >= BannerAfterDays
            ? Texts.F("Вы не тренировались {0} дн. Пять минут сегодня сохранят навык.", days.Value)
            : null;
    }

    /// <summary>Пора показать уведомление: время наступило, сегодня его ещё не показывали и ещё не занимались.</summary>
    public static bool IsReminderDue(DateTime now, int reminderMinutes, DateOnly? lastShown, bool trainedToday)
    {
        var today = DateOnly.FromDateTime(now);
        return lastShown != today && !trainedToday && now.TimeOfDay >= ReminderSchedule.ToTime(reminderMinutes);
    }

    /// <summary>Сколько дней занятий помнить для серии — больше года.</summary>
    public const int MaxPracticeDays = 400;

    /// <summary>Отмечает день занятия (в том числе на бумаге, без записи в истории): «yyyy-MM-dd» без повторов, по порядку.</summary>
    public static void MarkPracticeDay(List<string> days, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(days);
        var day = DateOnly.FromDateTime(now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!days.Contains(day))
        {
            days.Add(day);
        }

        days.Sort(StringComparer.Ordinal);
        if (days.Count > MaxPracticeDays)
        {
            days.RemoveRange(0, days.Count - MaxPracticeDays);
        }
    }

    /// <summary>Серия дней подряд — и по истории проверенных заданий, и по занятиям на бумаге и «На слух».</summary>
    public static PracticeStreak Streak(IReadOnlyList<TrainingRecord> records, IEnumerable<string>? practiceDays, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(records);
        var days = records.Select(item => DateOnly.FromDateTime(item.CompletedAt)).ToList();
        foreach (var text in practiceDays ?? Enumerable.Empty<string>())
        {
            if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                days.Add(day);
            }
        }

        return TrainingStatistics.Streak(days, today);
    }

    /// <summary>Текст напоминания: с серией от двух дней подряд — чтобы её не хотелось прерывать.</summary>
    public static string ReminderText(int streak) => streak >= 2
        ? Texts.F("Пора потренироваться: пять минут азбуки Морзе. Дней подряд: {0} — не прерывайте серию.", streak)
        : Texts.T("Пора потренироваться: пять минут азбуки Морзе.");

    /// <summary>Варианты времени для списка: каждые 30 минут от 00:00 до 23:30.</summary>
    public static IReadOnlyList<int> TimeChoices { get; } =
        Enumerable.Range(0, ReminderSchedule.MinutesPerDay / TimeStepMinutes).Select(index => index * TimeStepMinutes).ToArray();

    /// <summary>Ближайший вариант списка к сохранённому времени.</summary>
    public static int NearestChoiceIndex(int minutes) => (int)Math.Round(ReminderSchedule.ClampMinutes(minutes) / (double)TimeStepMinutes) % TimeChoices.Count;
}
