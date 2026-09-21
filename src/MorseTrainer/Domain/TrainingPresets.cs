using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>Готовые наборы параметров тренировки.</summary>
public static class TrainingPresets
{
    public const int FarnsworthCharactersPerMinute = 90;
    public const int FarnsworthCharacterGapUnits = 9;
    public const int FarnsworthGroupGapUnits = 21;

    public const string FarnsworthDescription =
        "Метод Фарнсворта: символы звучат быстро (не меньше 90 знаков в минуту), а паузы между ними растянуты. " +
        "Ухо запоминает ритм целого знака, а не считает точки и тире.";

    /// <summary>Скорость знака не ниже 90 зн/мин, паузы 9 точек между символами и 21 между группами.</summary>
    public static void ApplyFarnsworth(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.CharactersPerMinute = Math.Max(settings.CharactersPerMinute, FarnsworthCharactersPerMinute);
        settings.CharacterGapUnits = FarnsworthCharacterGapUnits;
        settings.GroupGapUnits = FarnsworthGroupGapUnits;
    }
}
