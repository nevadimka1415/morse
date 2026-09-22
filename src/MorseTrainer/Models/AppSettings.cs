namespace MorseTrainer.Models;

public sealed class AppSettings
{
    public int AlphabetIndex { get; set; } = 0;
    public int ContentModeIndex { get; set; } = 2;
    public int GroupCount { get; set; } = 10;
    public int CharactersPerMinute { get; set; } = 60;
    public int FrequencyHz { get; set; } = 700;
    public int VolumePercent { get; set; } = 70;
    public int CharacterGapUnits { get; set; } = 3;
    public int GroupGapUnits { get; set; } = 7;
    public int StartPauseUnits { get; set; } = 21;
    public bool PlayStartSignal { get; set; } = true;
    // Помехи эфира: шум, замирания, дрейф тона (0 — чистый сигнал)
    public int NoisePercent { get; set; }
    public int QsbPercent { get; set; }
    public int DriftHz { get; set; }
    public string CustomSymbols { get; set; } = "АБВГДЕЖЗИКЛМНОПРСТУ";
    public int KochLevel { get; set; } = 2;
    public bool EmphasizeProblemSymbols { get; set; } = true;
    public int ThemeIndex { get; set; } = 0;
    public int LanguageIndex { get; set; } = 0;
    public int LearningAlphabetIndex { get; set; } = 0;
    public int LearningAudioModeIndex { get; set; } = 1;
    public int QuizCorrect { get; set; }
    public int QuizTotal { get; set; }
    public string ActiveProfileName { get; set; } = "Основной";
}
