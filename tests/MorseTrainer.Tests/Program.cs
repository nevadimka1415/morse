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
    ("Embedded voice pack", TestVoicePack),
    ("Update check", TestUpdateCheck)
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
    Assert(VoiceClipCatalog.GetClipName('А') == "code_01", "Clip name for А must be code_01.");
    Assert(VoiceClipCatalog.GetClipName('Й') == VoiceClipCatalog.GetClipName('J'), "Й and J share the same Morse code and clip.");
    Assert(VoiceClipCatalog.GetClipName('W') == "code_011", "Clip name for W must be code_011.");
    Assert(VoiceClipCatalog.GetClipName('7') == "code_11000", "Clip name for 7 must be code_11000.");
    Assert(VoiceClipCatalog.GetClipName('~') is null, "Unknown symbol must have no clip.");

    // Оба голосовых пакета лежат в репозитории: проверяем файлы на диске, без Windows-ресурсов
    var root = FindRepositoryRoot();
    var required = VoiceClipCatalog.RequiredClipNames();
    Assert(required.Count == 42, $"Letters and digits must need 42 clips, got {required.Count}.");
    foreach (var name in required)
    {
        var wav = Path.Combine(root, "src", "MorseTrainer", "Assets", "Voice", name + ".wav");
        var m4a = Path.Combine(root, "src", "MorseTrainer.Mobile", "Resources", "Raw", "voice", name + ".m4a");
        Assert(File.Exists(wav) && new FileInfo(wav).Length > 44, $"Desktop voice clip is missing: {name}.wav");
        Assert(File.Exists(m4a) && new FileInfo(m4a).Length > 1000, $"Mobile voice clip is missing: {name}.m4a");
    }

    using var stream = File.OpenRead(Path.Combine(root, "src", "MorseTrainer", "Assets", "Voice", "code_01.wav"));
    var header = new byte[4];
    Assert(stream.Read(header, 0, 4) == 4 && Encoding.ASCII.GetString(header) == "RIFF", "Voice clip must be a WAV file.");
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "src", "MorseTrainer")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Repository root with src/MorseTrainer was not found.");
}

static void TestUpdateCheck()
{
    Assert(UpdateService.ParseVersion("v2.3.0") == new Version(2, 3, 0), "Tag v2.3.0 must parse to 2.3.0.");
    Assert(UpdateService.ParseVersion("2.10") == new Version(2, 10, 0), "Two-part version must get patch 0.");
    Assert(UpdateService.ParseVersion("release") is null, "Text without a number must not parse.");
    Assert(UpdateService.IsNewer(new Version(2, 2, 1, 0), new Version(2, 3, 0)), "2.3.0 must be newer than assembly 2.2.1.0.");
    Assert(!UpdateService.IsNewer(new Version(2, 3, 0, 0), new Version(2, 3, 0)), "Assembly 2.3.0.0 must equal tag 2.3.0.");
    Assert(!UpdateService.IsNewer(new Version(2, 3, 1), new Version(2, 3, 0)), "Older release must not be reported as newer.");

    const string json = @"{ ""tag_name"": ""v2.3.0"", ""html_url"": ""https://github.com/nevadimka1415/morse/releases/tag/v2.3.0"", " +
        @"""body"": ""- Проверка обновлений  "", ""assets"": [ " +
        @"{ ""name"": ""MorseTrainer-Android.apk"", ""browser_download_url"": ""https://example.test/MorseTrainer-Android.apk"" }, " +
        @"{ ""name"": ""MorseTrainer-Setup-x64.exe"", ""browser_download_url"": ""https://example.test/MorseTrainer-Setup-x64.exe"" }, " +
        @"{ ""name"": ""MorseTrainer-Windows-x64.zip"", ""browser_download_url"": ""https://example.test/MorseTrainer-Windows-x64.zip"" } ] }";
    var info = UpdateService.ParseRelease(json);
    Assert(info.LatestVersion == new Version(2, 3, 0), "Release version was not parsed.");
    Assert(info.AndroidApkUrl == "https://example.test/MorseTrainer-Android.apk", "APK asset was not found.");
    Assert(info.WindowsInstallerUrl == "https://example.test/MorseTrainer-Setup-x64.exe", "Installer asset was not found.");
    Assert(info.ReleasePageUrl.EndsWith("/v2.3.0", StringComparison.Ordinal), "Release page URL was not parsed.");
    Assert(info.Notes == "- Проверка обновлений", "Release notes must be trimmed.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
