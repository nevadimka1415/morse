using System.Globalization;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Mobile.Services;

/// <summary>
/// Текст уведомления; язык берётся из настроек, потому что приёмник может сработать без запуска приложения.
/// С серией от двух дней подряд (история и занятия на бумаге) — «Дней подряд: N — не прерывайте серию».
/// </summary>
public static class ReminderTexts
{
    public const string Title = "Morse Trainer";

    public static string Body(AppSettings settings, IReadOnlyList<TrainingRecord> history)
    {
        Texts.Apply((AppLanguage)Math.Clamp(settings.LanguageIndex, 0, 2), CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        var streak = PracticeNudge.Streak(history, settings.PracticeDays, DateOnly.FromDateTime(DateTime.Now)).Current;
        return PracticeNudge.ReminderText(streak);
    }
}
