using System.Globalization;
using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Mobile.Services;

/// <summary>Текст уведомления; язык берётся из настроек, потому что приёмник может сработать без запуска приложения.</summary>
public static class ReminderTexts
{
    public const string Title = "Morse Trainer";

    public static string Body(AppSettings settings)
    {
        Texts.Apply((AppLanguage)Math.Clamp(settings.LanguageIndex, 0, 2), CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        return Texts.T("Пора потренироваться: пять минут азбуки Морзе.");
    }
}
