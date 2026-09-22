namespace MorseTrainer.Models;

public sealed class TrainingProfile
{
    public string Name { get; set; } = "Основной";
    public int AlphabetIndex { get; set; }
    public int ContentModeIndex { get; set; } = 2;
    public int GroupCount { get; set; } = 10;
    public int CharactersPerMinute { get; set; } = 60;
    public int FrequencyHz { get; set; } = 700;
    public int VolumePercent { get; set; } = 70;
    public int CharacterGapUnits { get; set; } = 3;
    public int GroupGapUnits { get; set; } = 7;
    public int StartPauseUnits { get; set; } = 21;
    public bool PlayStartSignal { get; set; } = true;
    public int NoisePercent { get; set; }
    public int QsbPercent { get; set; }
    public int DriftHz { get; set; }
    public string CustomSymbols { get; set; } = "АБВГДЕЖЗИКЛМНОПРСТУ";
    public int KochLevel { get; set; } = 2;
    public bool EmphasizeProblemSymbols { get; set; } = true;
    public bool AutoSpeed { get; set; }

    public static TrainingProfile FromSettings(string name, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new TrainingProfile
        {
            Name = NormalizeName(name),
            AlphabetIndex = settings.AlphabetIndex,
            ContentModeIndex = settings.ContentModeIndex,
            GroupCount = settings.GroupCount,
            CharactersPerMinute = settings.CharactersPerMinute,
            FrequencyHz = settings.FrequencyHz,
            VolumePercent = settings.VolumePercent,
            CharacterGapUnits = settings.CharacterGapUnits,
            GroupGapUnits = settings.GroupGapUnits,
            StartPauseUnits = settings.StartPauseUnits,
            PlayStartSignal = settings.PlayStartSignal,
            NoisePercent = settings.NoisePercent,
            QsbPercent = settings.QsbPercent,
            DriftHz = settings.DriftHz,
            CustomSymbols = settings.CustomSymbols,
            KochLevel = settings.KochLevel,
            EmphasizeProblemSymbols = settings.EmphasizeProblemSymbols,
            AutoSpeed = settings.AutoSpeed
        };
    }

    public void ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.AlphabetIndex = AlphabetIndex;
        settings.ContentModeIndex = ContentModeIndex;
        settings.GroupCount = GroupCount;
        settings.CharactersPerMinute = CharactersPerMinute;
        settings.FrequencyHz = FrequencyHz;
        settings.VolumePercent = VolumePercent;
        settings.CharacterGapUnits = CharacterGapUnits;
        settings.GroupGapUnits = GroupGapUnits;
        settings.StartPauseUnits = StartPauseUnits;
        settings.PlayStartSignal = PlayStartSignal;
        settings.NoisePercent = NoisePercent;
        settings.QsbPercent = QsbPercent;
        settings.DriftHz = DriftHz;
        settings.CustomSymbols = CustomSymbols;
        settings.KochLevel = KochLevel;
        settings.EmphasizeProblemSymbols = EmphasizeProblemSymbols;
        settings.AutoSpeed = AutoSpeed;
        settings.ActiveProfileName = Name;
    }

    public static string NormalizeName(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Profile name must not be empty.", nameof(name));
        }

        return normalized.Length <= 40 ? normalized : normalized[..40];
    }
}
