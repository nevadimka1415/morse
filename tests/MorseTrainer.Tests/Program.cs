using System.Text;
using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Services;

var tests = new (string Name, Action Run)[]
{
    ("Alphabet counts", TestAlphabetCounts),
    ("Morse symbols", TestMorseSymbols),
    ("Custom pool", TestCustomPool),
    ("Learning chants", TestLearningChants),
    ("Training generation", TestGeneration),
    ("Answer evaluation", TestEvaluation),
    ("WAV rendering", TestWaveRendering),
    ("Start signal", TestStartSignal),
    ("Training profiles", TestTrainingProfile),
    ("Embedded voice pack", TestVoicePack)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}: {exception.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    return 1;
}

Console.WriteLine($"All {tests.Length} tests passed.");
return 0;

static void TestAlphabetCounts()
{
    Assert(MorseAlphabet.Russian.Count == 33, "Russian alphabet must contain 33 letters.");
    Assert(MorseAlphabet.Latin.Count == 26, "Latin alphabet must contain 26 letters.");
    Assert(MorseAlphabet.Digits.Count == 10, "Digits must contain 10 symbols.");
    Assert(MorseAlphabet.Punctuation.Count >= 10, "Punctuation must contain at least 10 symbols.");
}

static void TestMorseSymbols()
{
    var maps = new[]
    {
        MorseAlphabet.Russian,
        MorseAlphabet.Latin,
        MorseAlphabet.Digits,
        MorseAlphabet.Punctuation
    };

    foreach (var code in maps.SelectMany(map => map.Values))
    {
        Assert(code.Length > 0, "Morse code must not be empty.");
        Assert(code.All(symbol => symbol is '.' or '-'), $"Invalid Morse code: {code}");
    }
}

static void TestCustomPool()
{
    var pool = MorseAlphabet.BuildPool(AlphabetMode.Russian, ContentMode.Custom, " а а,б#1 ");
    Assert(pool.SequenceEqual(new[] { 'А', ',', 'Б', '1' }), "Custom pool must filter and deduplicate symbols.");
}

static void TestGeneration()
{
    var generated = TrainingGenerator.Generate(new[] { 'А', 'Б', '1' }, 4);
    var groups = generated.Split(' ');
    Assert(groups.Length == 4, "Generator must create the requested group count.");
    Assert(groups.All(group => group.Length == TrainingGenerator.GroupSize), "Every group must contain five symbols.");
    Assert(generated.Where(symbol => symbol != ' ').All(symbol => symbol is 'А' or 'Б' or '1'), "Generator used an unexpected symbol.");
}

static void TestLearningChants()
{
    Assert(LearningCatalog.Russian.Count == 33, "Learning mode must contain all Russian letters.");
    Assert(LearningCatalog.Latin.Count == 26, "Learning mode must contain all Latin letters.");
    Assert(LearningCatalog.Digits.Count == 10, "Learning mode must contain all digits.");

    foreach (var item in LearningCatalog.Russian.Concat(LearningCatalog.Latin).Concat(LearningCatalog.Digits))
    {
        Assert(!string.IsNullOrWhiteSpace(item.Chant), $"Missing chant for {item.Symbol}.");
        Assert(item.Chant.Split('-', StringSplitOptions.RemoveEmptyEntries).Length == item.Code.Length,
            $"Chant rhythm does not match Morse code for {item.Symbol}.");
    }

    Assert(LearningCatalog.Find('А')?.Chant == "ай-даа", "Russian A chant is incorrect.");
    Assert(LearningCatalog.Find('B')?.Chant == "баа-ки-те-кут", "Latin B chant is incorrect.");
    Assert(LearningCatalog.Find('Й')?.Chant == "ий-краааа-ткааааа-яяяяяя", "Russian short I chant is incorrect.");
    Assert(LearningCatalog.Find('К')?.Chant == "каааак-де-лаааааа", "Russian K chant is incorrect.");
    Assert(LearningCatalog.Find('Ц')?.Chant == "цаааап-ля-цааааап-ля", "Russian C chant is incorrect.");
    Assert(LearningCatalog.Find('Ь')?.Chant == "знааак-мяг-кий-знааааак", "Russian soft sign chant is incorrect.");
}

static void TestEvaluation()
{
    var exact = TrainingEvaluator.Evaluate("АБВ12 ГДЕ34", "абв12где34");
    Assert(exact.IsPerfect, "Evaluation must ignore case and whitespace.");
    Assert(Math.Abs(exact.AccuracyPercent - 100) < 0.01, "Exact answer must have 100% accuracy.");

    var mistake = TrainingEvaluator.Evaluate("АБВ", "АБГ");
    Assert(mistake.CorrectCount == 2, "Two symbols must be correct.");
    Assert(mistake.Mistakes.Count == 1 && mistake.Mistakes[0].Position == 3, "Mistake position is incorrect.");
}

static void TestWaveRendering()
{
    var clip = MorseAudioService.Render("SOS 12345", 60, 700, 70, 3, 7);
    Assert(clip.WavBytes.Length > 44, "WAV file must contain audio data.");
    Assert(Encoding.ASCII.GetString(clip.WavBytes, 0, 4) == "RIFF", "WAV file must start with RIFF.");
    Assert(Encoding.ASCII.GetString(clip.WavBytes, 8, 4) == "WAVE", "WAV file must contain WAVE signature.");
    Assert(clip.Duration > TimeSpan.Zero, "Audio duration must be positive.");
}

static void TestStartSignal()
{
    var plain = MorseAudioService.Render("АБВГД", 60, 700, 70, 3, 7);
    var withSignal = MorseAudioService.Render("АБВГД", 60, 700, 70, 3, 7, playStartSignal: true, startPauseUnits: 21);
    Assert(withSignal.Duration > plain.Duration + TimeSpan.FromSeconds(3),
        "The Ж Ж Ж start signal and its pause must be included before the task.");
}

static void TestTrainingProfile()
{
    var settings = new AppSettings
    {
        GroupCount = 24,
        CharactersPerMinute = 90,
        FrequencyHz = 800,
        VolumePercent = 55,
        CharacterGapUnits = 5,
        GroupGapUnits = 12,
        StartPauseUnits = 25,
        PlayStartSignal = true,
        CustomSymbols = "АГЖД"
    };
    var profile = TrainingProfile.FromSettings("Рабочий", settings);
    var restored = new AppSettings();
    profile.ApplyTo(restored);
    Assert(restored.ActiveProfileName == "Рабочий", "Profile name was not restored.");
    Assert(restored.GroupCount == 24 && restored.CharactersPerMinute == 90, "Profile timing was not restored.");
    Assert(restored.FrequencyHz == 800 && restored.VolumePercent == 55, "Profile audio was not restored.");
    Assert(restored.CustomSymbols == "АГЖД", "Profile symbols were not restored.");
}

static void TestVoicePack()
{
    using var russian = VoicePackService.Open('Й');
    using var latin = VoicePackService.Open('W');
    using var digit = VoicePackService.Open('7');
    Assert(russian is { Length: > 44 }, "Russian voice clip is missing.");
    Assert(latin is { Length: > 44 }, "Latin voice clip reuse is missing.");
    Assert(digit is { Length: > 44 }, "Digit voice clip is missing.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
