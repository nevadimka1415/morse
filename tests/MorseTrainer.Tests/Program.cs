using System.Text;
using System.Text.RegularExpressions;
using MorseTrainer.Domain;
using MorseTrainer.Localization;
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
    ("Update check", TestUpdateCheck),
    ("Training history", TestTrainingHistory),
    ("Koch method", TestKochMethod),
    ("Weighted generation", TestWeightedGeneration),
    ("Farnsworth preset", TestFarnsworthPreset),
    ("Profile transfer", TestProfileTransfer),
    ("Keyer decoder", TestKeyerDecoder),
    ("Loop tone", TestLoopTone),
    ("Localization", TestLocalization),
    ("Localization coverage", TestLocalizationCoverage),
    ("Crash report", TestCrashReport),
    ("Word lists", TestWordLists),
    ("Word tasks", TestWordTasks),
    ("Same-code evaluation", TestSameCodeEvaluation),
    ("Exam session", TestExam),
    ("Noise, QSB and drift", TestNoise),
    ("Speed ladder", TestSpeedLadder),
    ("Keyer analysis", TestKeyerAnalysis)
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
        CustomSymbols = "АГЖД",
        KochLevel = 7,
        EmphasizeProblemSymbols = false
    };
    var profile = TrainingProfile.FromSettings("Рабочий", settings);
    var restored = new AppSettings();
    profile.ApplyTo(restored);
    Assert(restored.ActiveProfileName == "Рабочий", "Profile name was not restored.");
    Assert(restored.GroupCount == 24 && restored.CharactersPerMinute == 90, "Profile timing was not restored.");
    Assert(restored.FrequencyHz == 800 && restored.VolumePercent == 55, "Profile audio was not restored.");
    Assert(restored.CustomSymbols == "АГЖД", "Profile symbols were not restored.");
    Assert(restored.KochLevel == 7 && !restored.EmphasizeProblemSymbols, "Profile Koch level and emphasis were not restored.");
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

static void TestTrainingHistory()
{
    var perfect = TrainingEvaluator.Evaluate("АБВГД", "АБВГД");
    var flawed = TrainingEvaluator.Evaluate("АБВГД", "АБВГЖ");
    var day1 = new DateTime(2026, 9, 21, 10, 0, 0);
    var first = TrainingStatistics.CreateRecord(day1, "Основной", 60, 1, flawed);
    Assert(first.ProblemSymbols == "Д" && Math.Abs(first.AccuracyPercent - 80) < 0.01, "Record must keep the mistaken symbol and accuracy.");

    var history = TrainingStatistics.Add(Array.Empty<TrainingRecord>(), first);
    history = TrainingStatistics.Add(history, TrainingStatistics.CreateRecord(day1.AddDays(1), "Быстрый", 90, 2, perfect));
    var summary = TrainingStatistics.Summarize(history);
    Assert(summary.Sessions == 2 && Math.Abs(summary.AverageAccuracy - 90) < 0.01 && Math.Abs(summary.BestAccuracy - 100) < 0.01,
        "History summary is wrong.");
    Assert(summary.TotalSymbols == 10 && summary.CorrectSymbols == 9, "Symbol totals are wrong.");
    var problems = TrainingStatistics.ProblemSymbols(history);
    Assert(problems.Count == 1 && problems[0].Symbol == 'Д' && problems[0].Count == 1, "Problem symbols are wrong.");
    var days = TrainingStatistics.ByDay(history);
    Assert(days.Count == 2 && days[0].Date == DateOnly.FromDateTime(day1) && days[1].Sessions == 1, "Daily grouping is wrong.");

    var overflow = history;
    for (var index = 0; index < TrainingStatistics.MaxRecords + 5; index++)
    {
        overflow = TrainingStatistics.Add(overflow, TrainingStatistics.CreateRecord(day1.AddMinutes(index), "Основной", 60, 1, perfect));
    }

    Assert(overflow.Count == TrainingStatistics.MaxRecords, "History must be trimmed to MaxRecords.");

    var directory = Path.Combine(Path.GetTempPath(), "morse-tests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new TrainingHistoryStore(Path.Combine(directory, "history.json"));
        Assert(store.Load().Count == 0, "Empty store must return no records.");
        store.Add(first);
        var loaded = store.Add(history[1]);
        Assert(loaded.Count == 2, "Store must append records.");
        Assert(new TrainingHistoryStore(Path.Combine(directory, "history.json")).Load()[1].ProfileName == "Быстрый", "Store must persist records.");
        store.Clear();
        Assert(store.Load().Count == 0, "Clear must remove the history.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void TestKochMethod()
{
    var russian = KochMethod.Sequence(AlphabetMode.Russian);
    var latin = KochMethod.Sequence(AlphabetMode.Latin);
    Assert(russian.Count == 42 && russian.Distinct().Count() == 42, "Russian Koch sequence must contain 42 unique symbols.");
    Assert(latin.Count == 36 && latin.Distinct().Count() == 36, "Latin Koch sequence must contain 36 unique symbols.");
    Assert(russian.Concat(latin).All(symbol => MorseAlphabet.TryGetCode(symbol, out _)), "Every Koch symbol must have a Morse code.");
    Assert(KochMethod.Sequence(AlphabetMode.RussianAndLatin).Count == 42 + 26, "Mixed sequence must add Latin letters after Russian.");
    Assert(new string(KochMethod.Pool(AlphabetMode.Russian, 5).ToArray()) == "КМРСУ", "Level 5 must contain the first five symbols.");
    Assert(KochMethod.Pool(AlphabetMode.Russian, 0).Count == KochMethod.MinLevel, "Level below minimum must be clamped.");
    Assert(KochMethod.NextSymbol(AlphabetMode.Russian, 5) == 'А', "Next symbol after level 5 must be А.");
    Assert(KochMethod.NextSymbol(AlphabetMode.Latin, 36) is null, "Last level has no next symbol.");
    Assert(MorseAlphabet.BuildPool(AlphabetMode.Latin, ContentMode.Koch, "", 3).SequenceEqual(new[] { 'K', 'M', 'R' }), "BuildPool must use the Koch level.");
    Assert(KochMethod.Advice(AlphabetMode.Russian, 5, 95).Contains("уровень 6"), "High accuracy must suggest the next level.");
    Assert(KochMethod.Advice(AlphabetMode.Russian, 5, 80).Contains("повторяйте уровень 5"), "Low accuracy must suggest repeating the level.");
}

static void TestWeightedGeneration()
{
    var generated = TrainingGenerator.Generate(new[] { 'А', 'Б' }, 100, new[] { 'А' }, emphasisWeight: 4);
    var countA = generated.Count(symbol => symbol == 'А');
    var countB = generated.Count(symbol => symbol == 'Б');
    Assert(countA + countB == 500, "Weighted generation must keep the group structure.");
    Assert(countA > countB * 2, $"Emphasized symbol must appear far more often (А={countA}, Б={countB}).");
    Assert(TrainingGenerator.Generate(new[] { 'А' }, 2, Array.Empty<char>()).Replace(" ", "") == "АААААААААА", "Empty emphasis must not change generation.");
}

static void TestFarnsworthPreset()
{
    var slow = new AppSettings { CharactersPerMinute = 40, CharacterGapUnits = 3, GroupGapUnits = 7 };
    TrainingPresets.ApplyFarnsworth(slow);
    Assert(slow.CharactersPerMinute == 90 && slow.CharacterGapUnits == 9 && slow.GroupGapUnits == 21, "Farnsworth must raise speed and stretch gaps.");
    var fast = new AppSettings { CharactersPerMinute = 120 };
    TrainingPresets.ApplyFarnsworth(fast);
    Assert(fast.CharactersPerMinute == 120, "Farnsworth must not slow down a faster setting.");
}

static void TestProfileTransfer()
{
    var fast = TrainingProfile.FromSettings("Быстрый", new AppSettings { CharactersPerMinute = 120, CustomSymbols = "АБВ" });
    var slow = TrainingProfile.FromSettings("Медленный", new AppSettings { CharactersPerMinute = 40 });
    var exported = ProfileTransfer.Export(new[] { fast, slow });
    Assert(exported.Contains("\"App\": \"MorseTrainer\"") && exported.Contains("Быстрый"), "Export must produce the MorseTrainer envelope.");

    var imported = ProfileTransfer.Import(exported);
    Assert(imported.Count == 2 && imported[0].Name == "Быстрый" && imported[0].CharactersPerMinute == 120, "Import must restore profiles from the envelope.");
    Assert(ProfileTransfer.Import("[{\"Name\":\"Тест\",\"CharactersPerMinute\":999,\"CustomSymbols\":\"а1?x\"}]") is [{ CharactersPerMinute: 300, CustomSymbols: "А1?X" }],
        "Bare array must be accepted and clamped.");

    var failed = false;
    try { ProfileTransfer.Import("не json"); } catch (FormatException) { failed = true; }
    Assert(failed, "Garbage must be rejected with FormatException.");
    failed = false;
    try { ProfileTransfer.Import("[{\"Name\":\"   \",\"CharactersPerMinute\":60}]"); } catch (FormatException) { failed = true; }
    Assert(failed, "Profiles with blank names must be rejected.");

    var existing = new[] { TrainingProfile.FromSettings("быстрый", new AppSettings { CharactersPerMinute = 60 }), TrainingProfile.FromSettings("Ночной", new AppSettings()) };
    var merged = ProfileTransfer.Merge(existing, imported);
    Assert(merged.Count == 3, "Merge must keep other profiles and replace the same name case-insensitively.");
    Assert(merged.Single(profile => profile.Name.Equals("Быстрый", StringComparison.OrdinalIgnoreCase)).CharactersPerMinute == 120, "Imported profile must replace the existing one.");
}

static void TestKeyerDecoder()
{
    Assert(MorseAlphabet.TryGetSymbol(".", AlphabetMode.Russian, out var russianE) && russianE == 'Е', "Dot must decode to Russian Е.");
    Assert(MorseAlphabet.TryGetSymbol("...", AlphabetMode.Latin, out var latinS) && latinS == 'S', "Three dots must decode to Latin S.");
    Assert(MorseAlphabet.TryGetSymbol("-----", AlphabetMode.Latin, out var zero) && zero == '0', "Digits must decode in any alphabet.");
    Assert(!MorseAlphabet.TryGetSymbol("......", AlphabetMode.Russian, out _), "Unknown code must not decode.");

    var keyer = new KeyerDecoder(AlphabetMode.Russian, 60);
    Assert(Math.Abs(keyer.UnitMilliseconds - 100) < 0.01, "60 cpm must give a 100 ms dot.");
    keyer.Press(80);
    keyer.Idle(100);
    keyer.Press(320);
    Assert(keyer.PendingCode == ".-" && keyer.Text.Length == 0, "Short and long presses must form dot and dash.");
    keyer.Idle(350);
    Assert(keyer.Text == "А" && keyer.PendingCode.Length == 0, "Three-unit pause must commit the symbol.");
    keyer.Idle(500);
    keyer.Idle(800);
    Assert(keyer.Text == "А ", "Seven-unit pause must add exactly one space.");
    keyer.Press(90);
    keyer.Idle(90);
    keyer.Press(90);
    keyer.Idle(90);
    keyer.Press(90);
    keyer.Idle(400);
    Assert(keyer.Text == "А С", "Dots after the gap must decode to С.");
    keyer.Press(90);
    keyer.Backspace();
    Assert(keyer.PendingCode.Length == 0 && keyer.Text == "А С", "Backspace must drop the pending code first.");
    keyer.Backspace();
    Assert(keyer.Text == "А ", "Backspace must then remove the last symbol.");
    keyer.Clear();
    Assert(keyer.Text.Length == 0, "Clear must reset the decoder.");
}

static void TestLoopTone()
{
    var tone = MorseAudioService.RenderTone(1, 700, 70);
    Assert(tone.WavBytes.Length > 44 && Encoding.ASCII.GetString(tone.WavBytes, 0, 4) == "RIFF", "Loop tone must be a WAV file.");
    Assert(Math.Abs(tone.Duration.TotalSeconds - 1) < 0.01, "Loop tone must be about one second long.");
    var first = BitConverter.ToInt16(tone.WavBytes, 44);
    var last = BitConverter.ToInt16(tone.WavBytes, tone.WavBytes.Length - 2);
    Assert(first == 0 && Math.Abs(last) < 2000, "Loop tone must start and end near zero crossing for a seamless loop.");
}

static void TestLocalization()
{
    Texts.Apply(AppLanguage.Russian);
    Assert(Texts.T("Тренировка") == "Тренировка", "Russian mode must keep Russian text.");
    Texts.Apply(AppLanguage.English);
    Assert(Texts.IsEnglish && Texts.T("Тренировка") == "Training", "English mode must translate.");
    Assert(Texts.T("нет такого ключа") == "нет такого ключа", "Missing translation must fall back to the key.");
    Assert(Texts.F("{0} Гц", 700) == "700 Hz", "F must format the translated template.");
    Texts.Apply(AppLanguage.System, "de");
    Assert(Texts.IsEnglish, "System language other than Russian must select English.");
    Texts.Apply(AppLanguage.System, "ru");
    Assert(!Texts.IsEnglish, "Russian system language must select Russian.");
    Texts.Apply(AppLanguage.Russian);
}

static void TestLocalizationCoverage()
{
    var root = FindRepositoryRoot();
    var missing = new SortedSet<string>(StringComparer.Ordinal);
    var used = 0;
    foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories))
    {
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{loc:Loc '([^']+)'\}"))
        {
            used++;
            if (!Texts.Translations.ContainsKey(match.Groups[1].Value)) missing.Add(match.Groups[1].Value);
        }
    }

    foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
    {
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"Texts\.[TF]\(""((?:[^""\\]|\\.)*)"""))
        {
            used++;
            var key = Regex.Unescape(match.Groups[1].Value);
            if (!Texts.Translations.ContainsKey(key)) missing.Add(key);
        }
    }

    Assert(used > 300, $"Localization markers must be present in XAML and code, found {used}.");
    Assert(missing.Count == 0, "Missing English translations: " + string.Join(" | ", missing.Take(15)));
}

static void TestCrashReport()
{
    var text = CrashReport.Format("Unit test", new InvalidOperationException("boom"), "2.4.0", "Test OS");
    Assert(text.Contains("Source: Unit test") && text.Contains("Version: 2.4.0") && text.Contains("Platform: Test OS"), "Report header is incomplete.");
    Assert(text.Contains("InvalidOperationException") && text.Contains("boom"), "Report must include the exception.");
    Assert(CrashReport.Format("x", null, "1", "p").Contains("Unknown exception"), "Missing exception must be reported as unknown.");

    var directory = Path.Combine(Path.GetTempPath(), "morse-crash-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, CrashReport.FileName);
    try
    {
        Assert(!CrashReport.Exists(path) && CrashReport.LastCrashAt(path) is null, "No report must exist before writing.");
        Assert(CrashReport.Write(path, "Test", new Exception("first"), "2.4.0", "Test OS"), "Write must create the directory and the file.");
        Assert(CrashReport.Exists(path) && CrashReport.LastCrashAt(path) is not null, "Report must exist after writing.");
        Assert(CrashReport.Read(path)!.Contains("first"), "Read must return the report text.");
        CrashReport.Delete(path);
        Assert(!CrashReport.Exists(path) && CrashReport.Read(path) is null, "Delete must remove the report.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void TestWordLists()
{
    Assert(WordLists.RussianWords.Count >= 150 && WordLists.EnglishWords.Count >= 150 && WordLists.QCodes.Count >= 80,
        $"Word lists are too short: {WordLists.RussianWords.Count}/{WordLists.EnglishWords.Count}/{WordLists.QCodes.Count}.");
    Assert(WordLists.RussianWords.All(word => word.All(symbol => MorseAlphabet.Russian.ContainsKey(symbol) && symbol != 'Ё')),
        "Russian words must use only Russian letters without Ё.");
    Assert(WordLists.EnglishWords.All(word => word.All(symbol => MorseAlphabet.Latin.ContainsKey(symbol))), "English words must use only Latin letters.");
    Assert(WordLists.QCodes.All(word => word.All(symbol => MorseAlphabet.TryGetCode(symbol, out _))), "Every Q-code symbol must have a Morse code.");
    Assert(WordLists.RussianWords.Distinct().Count() == WordLists.RussianWords.Count, "Russian words must be unique.");
    Assert(WordLists.Words(AlphabetMode.RussianAndLatin).Count == WordLists.RussianWords.Count + WordLists.EnglishWords.Count, "Mixed alphabet must combine both lists.");
    Assert(WordLists.Symbols(ContentMode.Words, AlphabetMode.Russian).Contains('Ъ') && WordLists.Symbols(ContentMode.Callsigns, AlphabetMode.Russian).SequenceEqual("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"),
        "Symbols must list the characters used by the mode.");

    var callsignPattern = new Regex("^[A-Z0-9]{1,3}[0-9][A-Z]{1,3}$");
    for (var index = 0; index < 200; index++)
    {
        var callsign = WordLists.RandomCallsign();
        Assert(callsignPattern.IsMatch(callsign), $"Callsign has an unexpected shape: {callsign}");
    }
}

static void TestWordTasks()
{
    var words = TrainingGenerator.GenerateTask(ContentMode.Words, AlphabetMode.Russian, Array.Empty<char>(), 7).Split(' ');
    Assert(words.Length == 7 && words.All(WordLists.RussianWords.Contains), "Words task must contain the requested number of Russian words.");
    var english = TrainingGenerator.GenerateTask(ContentMode.Words, AlphabetMode.Latin, Array.Empty<char>(), 5).Split(' ');
    Assert(english.All(WordLists.EnglishWords.Contains), "Latin alphabet must produce English words.");
    var codes = TrainingGenerator.GenerateTask(ContentMode.QCodes, AlphabetMode.Russian, Array.Empty<char>(), 6).Split(' ');
    Assert(codes.Length == 6 && codes.All(WordLists.QCodes.Contains), "Q-code task must use the Q-code list.");
    var callsigns = TrainingGenerator.GenerateTask(ContentMode.Callsigns, AlphabetMode.Russian, Array.Empty<char>(), 4).Split(' ');
    Assert(callsigns.Length == 4 && callsigns.All(item => item.Any(char.IsDigit)), "Callsign task must contain callsigns with a digit.");
    var groups = TrainingGenerator.GenerateTask(ContentMode.Letters, AlphabetMode.Russian, new[] { 'А', 'Б' }, 3).Split(' ');
    Assert(groups.Length == 3 && groups.All(group => group.Length == 5), "Other modes must keep groups of five.");

    var emphasized = TrainingGenerator.GenerateWords(new[] { "AAA", "BBB" }, 100, new[] { 'A' }, emphasisWeight: 5).Split(' ');
    Assert(emphasized.Count(word => word == "AAA") > 60, "Words with emphasized symbols must appear more often.");
    Assert(MorseAlphabet.BuildPool(AlphabetMode.Latin, ContentMode.Words, "").Count > 20, "BuildPool for word modes must list the used letters.");
    Assert(ContentModes.Clamp(99) == ContentMode.QCodes && ContentModes.Clamp(-1) == ContentMode.Letters, "ContentModes.Clamp must bound the index.");
    Assert(ContentModes.DecodingAlphabet(ContentMode.Callsigns, AlphabetMode.Russian) == AlphabetMode.Latin
           && ContentModes.DecodingAlphabet(ContentMode.Words, AlphabetMode.Russian) == AlphabetMode.Russian, "Callsigns must decode in Latin.");
}

static void TestSameCodeEvaluation()
{
    var mixed = TrainingEvaluator.Evaluate("ABC", "АБС");
    Assert(mixed.CorrectCount == 2 && mixed.Mistakes.Count == 1 && mixed.Mistakes[0].Position == 3,
        "A/А and B/Б share a code and must match; C/С do not.");
    Assert(TrainingEvaluator.Evaluate("ЁЛКА", "ЕЛКА").IsPerfect, "Е typed for Ё must be accepted.");
    Assert(TrainingEvaluator.Evaluate("R3ABC UA9XYZ", "r3abc ua9xyz").IsPerfect, "Callsigns must be compared case-insensitively.");
    Assert(!TrainingEvaluator.Matches('А', 'Б') && TrainingEvaluator.Matches('Р', 'R'), "Matches must compare Morse codes.");
}

static void TestExam()
{
    var started = new DateTime(2026, 9, 22, 12, 0, 0);
    var exam = new ExamSession("АБВГД ЕЖЗИК", 60, 2, "Основной", started);
    Assert(exam.CanPlay && exam.PlaybacksUsed == 0, "A new exam allows one playback.");
    exam.RegisterPlayback();
    Assert(!exam.CanPlay, "The second playback must be blocked.");
    var blocked = false;
    try { exam.RegisterPlayback(); } catch (InvalidOperationException) { blocked = true; }
    Assert(blocked, "RegisterPlayback must throw after the single playback.");
    Assert(exam.Elapsed(started.AddSeconds(75)) == TimeSpan.FromSeconds(75), "Elapsed must count from the start.");

    var result = exam.Finish("абвгд ежзиЛ", started.AddSeconds(95));
    Assert(exam.IsFinished && result.Duration == TimeSpan.FromSeconds(95) && result.Evaluation.CorrectCount == 9, "Finish must evaluate the answer and keep the duration.");
    Assert(ExamReport.FormatDuration(result.Duration) == "1:35", "Duration must be formatted as m:ss.");
    var bySymbol = ExamReport.MistakesBySymbol(result.Evaluation);
    Assert(bySymbol.Count == 1 && bySymbol[0].Symbol == 'К' && bySymbol[0].Count == 1, "Mistakes must be grouped by the expected symbol.");
    Texts.Apply(AppLanguage.Russian);
    var report = ExamReport.Format(result);
    Assert(report.Contains("Протокол экзамена") && report.Contains("90%") && report.Contains("1:35") && report.Contains("К ×1") && report.Contains("10: К→Л"),
        "Report must contain accuracy, time and mistakes: " + report);
    Assert(ExamReport.Summary(result).StartsWith("Экзамен: точность 90%"), "Summary must start with the accuracy.");
    var finishedTwice = false;
    try { exam.Finish("x", started); } catch (InvalidOperationException) { finishedTwice = true; }
    Assert(finishedTwice, "Finishing twice must throw.");

    var record = TrainingStatistics.CreateRecord(started, "Основной", 60, 2, result.Evaluation, isExam: true);
    Assert(record.IsExam && record.Kind == "Экзамен", "Exam record must be marked.");
    var plain = TrainingStatistics.CreateRecord(started, "Основной", 60, 2, result.Evaluation);
    Assert(!plain.IsExam && plain.Kind.Length == 0, "Ordinary record must not be marked.");
    var json = System.Text.Json.JsonSerializer.Serialize(new[] { record });
    Assert(json.Contains("\"IsExam\":true") && !json.Contains("Kind"), "IsExam is stored, Kind is not.");
    var legacy = System.Text.Json.JsonSerializer.Deserialize<List<TrainingRecord>>("[{\"CompletedAt\":\"2026-09-22T12:00:00\",\"ProfileName\":\"Основной\"}]")!;
    Assert(!legacy[0].IsExam, "Old history without IsExam must load as ordinary records.");
}

static void TestNoise()
{
    var clean = MorseAudioService.Render("ААААА", 60, 700, 70, 3, 7);
    var noisy = MorseAudioService.Render("ААААА", 60, 700, 70, 3, 7, noise: new NoiseProfile(60, 0, 0));
    Assert(noisy.WavBytes.Length == clean.WavBytes.Length, "Noise must not change the clip length.");
    Assert(TailRms(clean) < 1 && TailRms(noisy) > 200, $"Noise must fill the silent tail: clean {TailRms(clean):0}, noisy {TailRms(noisy):0}.");

    var longClean = MorseAudioService.Render("ААААА ААААА", 60, 700, 70, 3, 7);
    var faded = MorseAudioService.Render("ААААА ААААА", 60, 700, 70, 3, 7, noise: new NoiseProfile(0, 100, 0));
    Assert(faded.WavBytes.Length == longClean.WavBytes.Length && SumAbs(faded) < SumAbs(longClean) * 0.8,
        $"QSB must lower the average level: {SumAbs(faded):0} vs {SumAbs(longClean):0}.");

    var drifted = MorseAudioService.Render("ААААА", 60, 700, 70, 3, 7, noise: new NoiseProfile(0, 0, 50));
    Assert(drifted.WavBytes.Length == clean.WavBytes.Length && SumAbs(drifted) > SumAbs(clean) * 0.9, "Drift must keep the length and the level.");
    Assert(new NoiseProfile(500, -5, 99).Clamp() == new NoiseProfile(100, 0, 50), "Clamp must bound the noise profile.");
    Assert(NoiseProfile.None.IsClean && !new NoiseProfile(1, 0, 0).IsClean, "IsClean must detect a clean profile.");

    var settings = new AppSettings { NoisePercent = 30, QsbPercent = 40, DriftHz = 10 };
    var restored = new AppSettings();
    TrainingProfile.FromSettings("Шумный", settings).ApplyTo(restored);
    Assert(restored.NoisePercent == 30 && restored.QsbPercent == 40 && restored.DriftHz == 10, "Profile must carry the noise settings.");
    var imported = ProfileTransfer.Import("[{\"Name\":\"X\",\"NoisePercent\":999,\"QsbPercent\":-3,\"DriftHz\":80}]");
    Assert(imported[0].NoisePercent == 100 && imported[0].QsbPercent == 0 && imported[0].DriftHz == 50, "Import must clamp the noise settings.");
}

static double TailRms(AudioClip clip)
{
    const int samples = 2000;
    double sum = 0;
    for (var index = 0; index < samples; index++)
    {
        double value = BitConverter.ToInt16(clip.WavBytes, clip.WavBytes.Length - 2 * (index + 1));
        sum += value * value;
    }

    return Math.Sqrt(sum / samples);
}

static double SumAbs(AudioClip clip)
{
    double sum = 0;
    for (var offset = 44; offset + 1 < clip.WavBytes.Length; offset += 2)
    {
        sum += Math.Abs((double)BitConverter.ToInt16(clip.WavBytes, offset));
    }

    return sum;
}

static void TestSpeedLadder()
{
    var day = new DateTime(2026, 9, 22, 9, 0, 0);
    TrainingRecord Record(int minutes, int speed, double accuracy, string profile = "Основной") => new()
    {
        CompletedAt = day.AddMinutes(minutes), ProfileName = profile, CharactersPerMinute = speed, AccuracyPercent = accuracy, TotalCount = 10, CorrectCount = (int)(accuracy / 10)
    };

    Assert(SpeedLadder.Next(Array.Empty<TrainingRecord>(), 60) == 60, "Empty history keeps the speed.");
    Assert(SpeedLadder.Next(new[] { Record(1, 60, 95) }, 60) == 60, "One good task is not enough.");
    Assert(SpeedLadder.Next(new[] { Record(1, 60, 95), Record(2, 60, 92) }, 60) == 65, "Two good tasks in a row raise the speed by 5.");
    Assert(SpeedLadder.Next(new[] { Record(1, 60, 95), Record(2, 65, 92) }, 65) == 65, "The streak restarts after a raise: the older task was at a different speed.");
    Assert(SpeedLadder.Next(new[] { Record(1, 60, 95), Record(2, 60, 85), Record(3, 60, 95) }, 60) == 60, "A task below 90 % breaks the streak.");
    Assert(SpeedLadder.Next(new[] { Record(1, 60, 95), Record(2, 60, 60) }, 60) == 55, "A task below 70 % lowers the speed by 5.");
    Assert(SpeedLadder.Next(new[] { Record(1, 20, 10) }, 20) == 20 && SpeedLadder.Next(new[] { Record(1, 300, 100), Record(2, 300, 100) }, 300) == 300, "Speed stays within 20–300.");
    Assert(SpeedLadder.Next(new[] { Record(1, 60, 95, "Другой"), Record(2, 60, 95) }, 60, "Основной") == 60, "Other profiles are ignored.");
    Texts.Apply(AppLanguage.Russian);
    Assert(SpeedLadder.Describe(60, 60).Length == 0 && SpeedLadder.Describe(60, 65).Contains("60 → 65") && SpeedLadder.Describe(60, 55).Contains("ниже 70"), "Describe must explain the change.");

    var restored = new AppSettings();
    TrainingProfile.FromSettings("Лестница", new AppSettings { AutoSpeed = true }).ApplyTo(restored);
    Assert(restored.AutoSpeed, "Profile must carry the auto speed switch.");
}

static void TestKeyerAnalysis()
{
    Texts.Apply(AppLanguage.Russian);
    var keyer = new KeyerDecoder(AlphabetMode.Russian, 60);
    Assert(!keyer.Analyze().HasEnoughData && keyer.Analyze().Hints[0].StartsWith("Мало данных"), "Empty decoder must report not enough data.");

    // Идеальный ритм: А, С, М, пробел, Е — точки 100, тире 300, паузы 100/300/800
    keyer.Press(100); keyer.Idle(100); keyer.Press(300);
    keyer.Idle(50); keyer.Idle(300); keyer.Press(100); keyer.Idle(100); keyer.Press(100); keyer.Idle(100); keyer.Press(100);
    keyer.Idle(300); keyer.Press(300); keyer.Idle(100); keyer.Press(300);
    keyer.Idle(400); keyer.Idle(800); keyer.Press(100);
    keyer.Idle(300);
    Assert(keyer.Text == "АСМ Е", "Decoded text must not change: " + keyer.Text);
    var ideal = keyer.Analyze();
    Assert(ideal.DotCount == 5 && ideal.DashCount == 3, $"Ideal keying must count 5 dots and 3 dashes, got {ideal.DotCount}/{ideal.DashCount}.");
    Assert(Math.Abs(ideal.DashDotRatio - 3) < 0.01 && ideal.DotSpreadPercent < 0.01, "Ideal ratio must be 3:1 with no spread.");
    Assert(Math.Abs(ideal.AverageElementGapUnits - 1) < 0.01 && Math.Abs(ideal.AverageSymbolGapUnits - 3) < 0.01 && Math.Abs(ideal.AverageGroupGapUnits - 8) < 0.01,
        $"Gaps must be classified by their role: {ideal.AverageElementGapUnits}/{ideal.AverageSymbolGapUnits}/{ideal.AverageGroupGapUnits}.");
    Assert(ideal.HasEnoughData && ideal.Hints.Count == 1 && ideal.Hints[0].StartsWith("Ритм ровный"), "Ideal keying must get the steady-rhythm hint: " + string.Join(" | ", ideal.Hints));
    Assert(ideal.Describe().Contains("3.0:1") || ideal.Describe().Contains("3,0:1"), "Describe must show the ratio: " + ideal.Describe());

    // Плохой ритм: короткие тире, неровные точки, затянутые паузы внутри символа
    var sloppy = new KeyerDecoder(AlphabetMode.Latin, 60);
    sloppy.Press(100); sloppy.Idle(250); sloppy.Press(200);
    sloppy.Idle(300); sloppy.Press(200); sloppy.Idle(250); sloppy.Press(200);
    sloppy.Idle(300); sloppy.Press(200); sloppy.Idle(250); sloppy.Press(100);
    sloppy.Idle(300); sloppy.Press(100); sloppy.Idle(250); sloppy.Press(200);
    var bad = sloppy.Analyze();
    Assert(bad.HasEnoughData && bad.DashDotRatio < 2.5, $"Short dashes must give a low ratio: {bad.DashDotRatio}.");
    Assert(bad.Hints.Any(hint => hint.StartsWith("Тире коротковаты")) && bad.Hints.Any(hint => hint.StartsWith("Паузы внутри символа затянуты")),
        "Sloppy keying must get dash and gap hints: " + string.Join(" | ", bad.Hints));

    var uneven = new KeyerDecoder(AlphabetMode.Latin, 60);
    foreach (var press in new[] { 60, 160, 60, 160, 300, 300 })
    {
        uneven.Press(press);
        uneven.Idle(100);
    }

    Assert(uneven.Analyze().DotSpreadPercent > 30 && uneven.Analyze().Hints.Any(hint => hint.StartsWith("Точки неровные")), "Uneven dots must be reported.");

    keyer.Clear();
    Assert(keyer.Analyze().DotCount == 0 && !keyer.Analyze().HasEnoughData, "Clear must reset the statistics.");
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
