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
    ("Keyer analysis", TestKeyerAnalysis),
    ("Progress filter and CSV", TestProgressCsv),
    ("Reminder schedule", TestReminderSchedule),
    ("Problem symbol drill", TestProblemDrill),
    ("Exam rules and series", TestExamRulesAndSeries),
    ("Daily goal, streak and speed", TestDailyGoalStreakSpeed),
    ("History transfer", TestHistoryTransfer),
    ("Custom chants and voice", TestCustomChantsAndVoice),
    ("Windows practice nudge", TestPracticeNudge),
    ("Course from zero to 60 cpm", TestCourse),
    ("Keyer target highlight", TestKeyerProgress),
    ("Listening quiz", TestEarQuiz),
    ("Easter egg", TestEasterEgg),
    ("Group counter", TestGroupCounter),
    ("Course book", TestCourseBook)
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
    Assert(MorseAlphabet.Russian.Count == 32 && !MorseAlphabet.Russian.ContainsKey('Ё'), "Russian alphabet must contain 32 letters without Ё (sent as Е).");
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
    Assert(LearningCatalog.Russian.Count == 32 && LearningCatalog.Russian.All(item => item.Symbol != 'Ё'), "Learning mode must contain all Russian letters without Ё.");
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
    Assert(TrainingEvaluator.Evaluate("ЕЛКА", "ёлка").IsPerfect, "Ё typed for Е must be accepted.");
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

static void TestProgressCsv()
{
    Texts.Apply(AppLanguage.Russian);
    var day = new DateTime(2026, 9, 22, 9, 30, 0);
    var perfect = TrainingEvaluator.Evaluate("АБВГД", "АБВГД");
    var flawed = TrainingEvaluator.Evaluate("АБВГД", "АБВГЖ");
    var records = new[]
    {
        TrainingStatistics.CreateRecord(day, "Основной", 60, 1, flawed),
        TrainingStatistics.CreateRecord(day.AddHours(1), "Быстрый", 90, 1, perfect, isExam: true),
        TrainingStatistics.CreateRecord(day.AddHours(2), "основной", 65, 1, perfect)
    };
    var names = TrainingStatistics.ProfileNames(records);
    Assert(names.Count == 2 && names[0] == "Быстрый" && names[1] == "Основной", "Profile names must be distinct (case-insensitive) and sorted: " + string.Join(",", names));
    Assert(TrainingStatistics.ForProfile(records, "ОСНОВНОЙ").Count == 2 && TrainingStatistics.ForProfile(records, null).Count == 3 && TrainingStatistics.ForProfile(records, "").Count == 3,
        "ForProfile must filter case-insensitively and return everything for an empty name.");

    var csv = TrainingStatistics.ToCsv(records);
    var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')).ToArray();
    Assert(lines.Length == 4 && lines[0].StartsWith("Дата;Профиль;Экзамен;Скорость (зн/мин);Групп;Символов;Верно;Точность (%);Ошибки"), "CSV header is wrong: " + lines[0]);
    Assert(lines[1].StartsWith("2026-09-22 09:30;Основной;;60;1;5;4;80;Д"), "CSV row is wrong: " + lines[1]);
    Assert(lines[2].Contains(";Быстрый;да;90;"), "Exam row must be marked: " + lines[2]);
    Assert(TrainingStatistics.ToCsv(new[] { TrainingStatistics.CreateRecord(day, "А;Б", 60, 1, perfect) }).Contains("\"А;Б\""), "Semicolon in a name must be quoted.");
    Texts.Apply(AppLanguage.English);
    Assert(TrainingStatistics.ToCsv(records).StartsWith("Date;Profile;Exam;Speed (cpm);Groups;Symbols;Correct;Accuracy (%);Mistakes"), "CSV header must be translated.");
    Texts.Apply(AppLanguage.Russian);
}

static void TestReminderSchedule()
{
    Assert(ReminderSchedule.DefaultMinutes == 19 * 60 && new AppSettings().ReminderMinutes == ReminderSchedule.DefaultMinutes && !new AppSettings().ReminderEnabled,
        "Default reminder is 19:00 and off.");
    Assert(ReminderSchedule.ToTime(19 * 60 + 30) == new TimeSpan(19, 30, 0) && ReminderSchedule.ToMinutes(new TimeSpan(7, 5, 0)) == 425, "Minutes and time must convert both ways.");
    Assert(ReminderSchedule.ClampMinutes(-10) == 1430 && ReminderSchedule.ClampMinutes(1500) == 60, "Minutes must wrap around the day.");
    var now = new DateTime(2026, 9, 22, 18, 0, 0);
    Assert(ReminderSchedule.NextOccurrence(now, new TimeSpan(19, 0, 0)) == new DateTime(2026, 9, 22, 19, 0, 0), "Later today must fire today.");
    Assert(ReminderSchedule.NextOccurrence(now, new TimeSpan(9, 0, 0)) == new DateTime(2026, 9, 23, 9, 0, 0), "Earlier time must fire tomorrow.");
    Assert(ReminderSchedule.NextOccurrence(now, new TimeSpan(18, 0, 0)) == new DateTime(2026, 9, 23, 18, 0, 0), "Exactly now fires tomorrow.");
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

static void TestProblemDrill()
{
    Assert(ProblemDrill.EditDistance("...", "...") == 0 && ProblemDrill.EditDistance("...", "....") == 1
           && ProblemDrill.EditDistance(".-", "-.") == 2, "Edit distance between codes is wrong.");

    // Похожие на С (...): отличие в один элемент, той же длины — раньше
    var similarToS = ProblemDrill.SimilarSymbols('С', 3);
    Assert(similarToS.Count == 3 && !similarToS.Contains('С'), "С must get three similar symbols.");
    foreach (var symbol in similarToS)
    {
        MorseAlphabet.TryGetCode(symbol, out var code);
        Assert(ProblemDrill.EditDistance("...", code) == 1 && code.Length == 3, $"{symbol} ({code}) is not the closest to С.");
        Assert(MorseAlphabet.Russian.ContainsKey(symbol), "Similar symbols for a Russian letter must be Russian letters.");
    }

    Assert(ProblemDrill.SimilarSymbols('A', 4).All(symbol => MorseAlphabet.Latin.ContainsKey(symbol)), "Similar symbols for a Latin letter must be Latin.");
    Assert(ProblemDrill.SimilarSymbols('5', 2).All(char.IsDigit), "Similar symbols for a digit must be digits.");
    Assert(ProblemDrill.SimilarSymbols('#', 2).Count == 0, "Unknown symbol must have no similar symbols.");

    var at = new DateTime(2026, 9, 23, 10, 0, 0);
    Assert(ProblemDrill.Build(Array.Empty<TrainingRecord>()).IsEmpty, "Empty history must give an empty drill.");
    var history = new[]
    {
        new TrainingRecord { CompletedAt = at, ProblemSymbols = "ССС" },
        new TrainingRecord { CompletedAt = at.AddMinutes(1), ProblemSymbols = "Д" },
        // Латинская S звучит как С: второй раз её брать незачем
        new TrainingRecord { CompletedAt = at.AddMinutes(2), ProblemSymbols = "S" }
    };
    var plan = ProblemDrill.Build(history);
    Assert(plan.Problems.SequenceEqual(new[] { 'С', 'Д' }), "Problems must be ordered by count and deduplicated by code: " + string.Join(",", plan.Problems));
    Assert(plan.Similar.Count == 4, "Each problem symbol must bring two similar ones: " + string.Join(",", plan.Similar));
    var codes = plan.Pool.Select(symbol => MorseAlphabet.TryGetCode(symbol, out var code) ? code : "").ToArray();
    Assert(codes.Distinct().Count() == codes.Length, "Drill pool must not repeat a Morse code.");
    Assert(plan.Describe().Contains("С Д"), "Description must list the problem symbols: " + plan.Describe());

    var task = TrainingGenerator.Generate(plan.Pool, 20, plan.Problems);
    Assert(task.Where(symbol => symbol != ' ').All(plan.Pool.Contains), "Drill task must use only the drill symbols.");
    var problemShare = task.Count(plan.Problems.Contains) / (double)task.Count(symbol => symbol != ' ');
    Assert(problemShare > 0.4, $"Problem symbols must play more often than similar ones: {problemShare:0.00}.");

    // Учитываются только последние RecentRecords заданий: старые ошибки уже исправлены
    var old = Enumerable.Range(0, 50).Select(index => new TrainingRecord { CompletedAt = at.AddMinutes(index), ProblemSymbols = "Ж" });
    var fresh = Enumerable.Range(0, ProblemDrill.RecentRecords).Select(index => new TrainingRecord { CompletedAt = at.AddHours(2).AddMinutes(index) });
    Assert(ProblemDrill.Build(old.Concat(fresh).ToArray()).IsEmpty, "Old mistakes outside the recent window must be ignored.");
}

static void TestExamRulesAndSeries()
{
    Texts.Apply(AppLanguage.Russian);
    Assert(ExamSession.ClampPlaybacks(0) == 1 && ExamSession.ClampPlaybacks(7) == 3 && ExamSession.ClampTimeLimit(99) == 30 && ExamSession.ClampTimeLimit(-5) == 0,
        "Exam rules must be clamped to 1–3 playbacks and 0–30 minutes.");

    var started = new DateTime(2026, 9, 23, 12, 0, 0);
    var free = new ExamSession("АБВГД", 60, 1, "Основной", started);
    Assert(free.MaxPlaybacks == 1 && free.TimeLimit is null && free.Remaining(started) is null && !free.IsTimeUp(started.AddHours(1)),
        "Default exam has one playback and no time limit.");
    Assert(free.Status(started.AddSeconds(5)) == "Экзамен · прослушано 0 из 1 · 0:05", "Status without a limit shows the elapsed time: " + free.Status(started.AddSeconds(5)));

    var exam = new ExamSession("АБВГД ЕЖЗИК", 60, 2, "Основной", started, maxPlaybacks: 2, timeLimitMinutes: 1);
    exam.RegisterPlayback();
    Assert(exam.CanPlay, "The second playback must be allowed with two playbacks.");
    exam.RegisterPlayback();
    Assert(!exam.CanPlay, "The third playback must be blocked.");
    Assert(exam.Status(started.AddSeconds(15)) == "Экзамен · прослушано 2 из 2 · осталось 0:45", "Status with a limit shows the remaining time: " + exam.Status(started.AddSeconds(15)));
    Assert(!exam.IsTimeUp(started.AddSeconds(59)) && exam.IsTimeUp(started.AddSeconds(60)), "Time is up exactly at the limit.");
    Assert(exam.Remaining(started.AddMinutes(5)) == TimeSpan.Zero, "Remaining time must not go below zero.");

    // Проверка позже лимита (приложение было свёрнуто): время в протоколе — ровно лимит
    var result = exam.Finish("АБВГД", started.AddSeconds(90));
    Assert(result.TimedOut && result.Duration == TimeSpan.FromMinutes(1) && result.PlaybacksUsed == 2 && result.MaxPlaybacks == 2,
        "Late finish must be marked as timed out and capped at the limit.");
    var report = ExamReport.Format(result);
    Assert(report.Contains("Прослушиваний: 2 из 2") && report.Contains("Лимит времени: 1 мин") && report.Contains("Время вышло"),
        "Report must list the rules and the timeout: " + report);
    Assert(ExamReport.Summary(result).Contains("Время вышло"), "Summary must mention the timeout.");
    Assert(ExamReport.Rules(2, TimeSpan.FromMinutes(5)) == "прослушиваний: 2 · лимит 5 мин" && ExamReport.Rules(1, null) == "прослушиваний: 1 · без лимита времени",
        "Rules line is wrong.");
    var inTime = new ExamSession("АБВГД", 60, 1, "Основной", started, timeLimitMinutes: 2).Finish("АБВГД", started.AddSeconds(30));
    Assert(!inTime.TimedOut && inTime.Duration == TimeSpan.FromSeconds(30) && !ExamReport.Format(inTime).Contains("Время вышло"), "An answer in time is not a timeout.");

    // Серия экзаменов: последние пять, тренд — наклон прямой
    Assert(TrainingStatistics.Exams(Array.Empty<TrainingRecord>()).Describe() == "Экзаменов пока нет", "Empty series text is wrong.");
    var accuracies = new[] { 50.0, 80, 85, 90, 95, 100 };
    var history = accuracies.Select((accuracy, index) => new TrainingRecord { CompletedAt = started.AddDays(index), IsExam = true, AccuracyPercent = accuracy })
        .Append(new TrainingRecord { CompletedAt = started.AddDays(10), AccuracyPercent = 10 })
        .ToArray();
    var series = TrainingStatistics.Exams(history);
    Assert(series.Exams.Count == 5 && series.Exams[0].AccuracyPercent == 80 && Math.Abs(series.TrendPerExam - 5) < 0.01,
        $"Series must take the last five exams with trend +5: {series.Exams.Count}, {series.TrendPerExam}.");
    Assert(series.Describe().Contains("80% · 85% · 90% · 95% · 100%") && series.Describe().Contains("↑ +5"), "Series text is wrong: " + series.Describe());
    var falling = TrainingStatistics.Exams(new[] { 90.0, 70 }.Select((accuracy, index) => new TrainingRecord { CompletedAt = started.AddDays(index), IsExam = true, AccuracyPercent = accuracy }).ToArray());
    Assert(falling.Describe().Contains("↓ −20"), "Falling series must show a negative trend: " + falling.Describe());
    var flat = TrainingStatistics.Exams(new[] { 90.0, 90.2 }.Select((accuracy, index) => new TrainingRecord { CompletedAt = started.AddDays(index), IsExam = true, AccuracyPercent = accuracy }).ToArray());
    Assert(flat.Describe().Contains("ровно"), "A tiny change must be shown as flat: " + flat.Describe());
    var single = TrainingStatistics.Exams(history.Take(1).ToArray());
    Assert(single.Exams.Count == 1 && !single.Describe().Contains("тренд"), "One exam has no trend.");
}

static void TestDailyGoalStreakSpeed()
{
    Texts.Apply(AppLanguage.Russian);
    var perfect = TrainingEvaluator.Evaluate("АБВГД", "АБВГД");
    var today = new DateOnly(2026, 9, 23);
    var noon = today.ToDateTime(new TimeOnly(12, 0));
    Assert(TrainingStatistics.CreateRecord(noon, "Основной", 60, 1, perfect, duration: TimeSpan.FromSeconds(90)).DurationSeconds == 90, "Duration must be stored in seconds.");
    Assert(TrainingStatistics.CreateRecord(noon, "Основной", 60, 1, perfect, duration: TimeSpan.FromHours(2)).DurationSeconds == TrainingStatistics.MaxTaskMinutes * 60,
        "A forgotten task must be capped at MaxTaskMinutes.");
    Assert(TrainingStatistics.CreateRecord(noon, "Основной", 60, 1, perfect).DurationSeconds == 0, "Duration is optional.");
    Assert(Math.Abs(TrainingStatistics.PracticeMinutes(new TrainingRecord { DurationSeconds = 120 }) - 2) < 0.001, "Measured minutes are wrong.");
    Assert(Math.Abs(TrainingStatistics.PracticeMinutes(new TrainingRecord { TotalCount = 90, CharactersPerMinute = 60 }) - 1.5) < 0.001,
        "Old records without a duration must be estimated by the listening time.");
    var legacy = System.Text.Json.JsonSerializer.Deserialize<List<TrainingRecord>>("[{\"CompletedAt\":\"2026-09-22T12:00:00\",\"TotalCount\":60,\"CharactersPerMinute\":60}]")!;
    Assert(legacy[0].DurationSeconds == 0 && Math.Abs(TrainingStatistics.PracticeMinutes(legacy[0]) - 1) < 0.001, "History from 2.4.0 must load without durations.");

    // Цель на день: 5 + 6,5 минут сегодня, вчерашние не считаются
    var records = new[]
    {
        new TrainingRecord { CompletedAt = noon, DurationSeconds = 300, AccuracyPercent = 95, CharactersPerMinute = 60 },
        new TrainingRecord { CompletedAt = noon.AddHours(1), DurationSeconds = 390, AccuracyPercent = 85, CharactersPerMinute = 80 },
        new TrainingRecord { CompletedAt = noon.AddDays(-1), DurationSeconds = 600, AccuracyPercent = 92, CharactersPerMinute = 70 },
        new TrainingRecord { CompletedAt = noon.AddDays(-3), DurationSeconds = 60, AccuracyPercent = 40, CharactersPerMinute = 100 }
    };
    var goal = TrainingStatistics.Goal(records, 10, today);
    Assert(goal.IsMet && Math.Abs(goal.TodayMinutes - 11.5) < 0.01 && goal.Progress == 1, $"Goal of 10 minutes must be met with 11.5: {goal.TodayMinutes}.");
    Assert(goal.Describe() == "Сегодня: 11,5 из 10 мин — цель выполнена ✓" || goal.Describe() == "Сегодня: 11.5 из 10 мин — цель выполнена ✓", "Goal text is wrong: " + goal.Describe());
    var harder = TrainingStatistics.Goal(records, 15, today);
    Assert(!harder.IsMet && Math.Abs(harder.Progress - 11.5 / 15) < 0.01, "Goal progress must be a fraction of the goal.");
    var none = TrainingStatistics.Goal(records, 0, today);
    Assert(!none.IsEnabled && none.Describe().Contains("цель не задана"), "Zero goal means no goal.");
    Assert(TrainingStatistics.ClampGoal(500) == 60 && TrainingStatistics.ClampGoal(-1) == 0, "Goal must be clamped to 0–60.");
    var byDay = TrainingStatistics.ByDay(records);
    Assert(Math.Abs(byDay[^1].Minutes - 11.5) < 0.01 && Math.Abs(byDay[^2].Minutes - 10) < 0.01, "Daily minutes are wrong.");

    // Скорость дня — только задания с точностью от 90 %
    var speeds = TrainingStatistics.SpeedByDay(records);
    Assert(speeds.Count == 2 && speeds[0].CharactersPerMinute == 70 && speeds[1].CharactersPerMinute == 60 && speeds[1].Date == today,
        "Speed by day must use the best speed among accurate tasks: " + string.Join(", ", speeds));

    // Серия: сегодня, вчера и три дня назад → текущая 2, рекорд 2
    var streak = TrainingStatistics.Streak(records, today);
    Assert(streak.Current == 2 && streak.Best == 2 && streak.TrainedToday, $"Streak is wrong: {streak}.");
    var yesterdayOnly = TrainingStatistics.Streak(records.Skip(2).ToArray(), today);
    Assert(yesterdayOnly.Current == 1 && !yesterdayOnly.TrainedToday && yesterdayOnly.Describe().Contains("сегодня ещё не занимались"),
        "A streak must survive until the end of today: " + yesterdayOnly.Describe());
    var longAgo = Enumerable.Range(6, 5).Select(days => new TrainingRecord { CompletedAt = noon.AddDays(-days) }).Append(new TrainingRecord { CompletedAt = noon }).ToArray();
    var broken = TrainingStatistics.Streak(longAgo, today);
    Assert(broken.Current == 1 && broken.Best == 5 && broken.Describe() == "Дней подряд: 1 · рекорд 5", "Best streak must be kept: " + broken.Describe());
    Assert(TrainingStatistics.Streak(Array.Empty<TrainingRecord>(), today) == new PracticeStreak(0, 0, false), "Empty history has no streak.");
}

static void TestHistoryTransfer()
{
    Texts.Apply(AppLanguage.Russian);
    var at = new DateTime(2026, 9, 23, 10, 0, 0, 123);
    var records = new[]
    {
        new TrainingRecord { CompletedAt = at, ProfileName = "Основной", CharactersPerMinute = 60, GroupCount = 2, TotalCount = 10, CorrectCount = 9, AccuracyPercent = 90, ProblemSymbols = "Ж", DurationSeconds = 95 },
        new TrainingRecord { CompletedAt = at.AddMinutes(5), ProfileName = "Быстрый", IsExam = true, CharactersPerMinute = 90, GroupCount = 1, TotalCount = 5, CorrectCount = 5, AccuracyPercent = 100 }
    };
    var json = HistoryTransfer.Export(records);
    Assert(json.Contains("\"Kind\":\"history\"") && json.Contains("Основной") && !json.Contains("\\u04"), "Export must be a readable history envelope: " + json[..Math.Min(200, json.Length)]);
    Assert(!json.Contains("Profiles"), "History export must not carry an empty Profiles field.");
    var imported = HistoryTransfer.Import(json);
    Assert(imported.Count == 2 && imported[0].ProblemSymbols == "Ж" && imported[0].DurationSeconds == 95 && imported[1].IsExam && imported[1].ProfileName == "Быстрый",
        "Round trip must keep all fields.");
    Assert(HistoryTransfer.Import(System.Text.Json.JsonSerializer.Serialize(records)).Count == 2, "A bare array of records must be accepted.");

    // Слияние: повторы узнаются по времени до секунды (миллисекунды на другом устройстве могут потеряться)
    var copy = new TrainingRecord { CompletedAt = at.AddMilliseconds(-100), ProfileName = "основной", CharactersPerMinute = 60, TotalCount = 10, CorrectCount = 9, AccuracyPercent = 90 };
    var fresh = new TrainingRecord { CompletedAt = at.AddDays(-1), ProfileName = "Основной", TotalCount = 5, CorrectCount = 4, AccuracyPercent = 80 };
    var merged = HistoryTransfer.Merge(records, new[] { copy, fresh });
    Assert(merged.Added == 1 && merged.Duplicates == 1 && merged.Trimmed == 0 && merged.History.Count == 3 && merged.History[0] == fresh,
        $"Merge must skip duplicates and sort by time: +{merged.Added} dup {merged.Duplicates}.");
    Assert(merged.Describe() == "Добавлено записей: 1, пропущено повторов: 1.", "Merge text is wrong: " + merged.Describe());
    var many = Enumerable.Range(0, TrainingStatistics.MaxRecords).Select(index => new TrainingRecord { CompletedAt = at.AddMinutes(index), TotalCount = 1, CorrectCount = 1 }).ToArray();
    var older = Enumerable.Range(1, 10).Select(index => new TrainingRecord { CompletedAt = at.AddDays(-index), TotalCount = 1, CorrectCount = 1 }).ToArray();
    var full = HistoryTransfer.Merge(many, older);
    Assert(full.History.Count == TrainingStatistics.MaxRecords && full.Trimmed == 10 && full.History[0].CompletedAt == at && full.Describe().Contains("удалены: 10"),
        "Merge must keep the newest MaxRecords records.");

    // Ошибки и чистка значений
    foreach (var bad in new[] { "", "не json", "{\"Records\":[]}", "[{\"Name\":\"Основной\"}]" })
    {
        var failed = false;
        try { HistoryTransfer.Import(bad); } catch (FormatException) { failed = true; }
        Assert(failed, "Import must reject: " + bad);
    }

    var profilesText = ProfileTransfer.Export(new[] { new TrainingProfile { Name = "Основной" } });
    try
    {
        HistoryTransfer.Import(profilesText);
        Assert(false, "Profiles must not be imported as history.");
    }
    catch (FormatException exception)
    {
        Assert(exception.Message.Contains("профили"), "Profiles file must get a clear message: " + exception.Message);
    }

    var dirty = HistoryTransfer.Import("[{\"CompletedAt\":\"2026-09-23T10:00:00\",\"ProfileName\":\" \",\"TotalCount\":5,\"CorrectCount\":9,\"AccuracyPercent\":250,\"CharactersPerMinute\":9999,\"ProblemSymbols\":\"Ж#\",\"DurationSeconds\":99999}]")[0];
    Assert(dirty.ProfileName == "Основной" && dirty.CorrectCount == 5 && dirty.AccuracyPercent == 100 && dirty.CharactersPerMinute == 300
           && dirty.ProblemSymbols == "Ж" && dirty.DurationSeconds == TrainingStatistics.MaxTaskMinutes * 60, "Imported values must be clamped.");

    var directory = Path.Combine(Path.GetTempPath(), "morse-transfer-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new TrainingHistoryStore(Path.Combine(directory, "history.json"));
        store.Add(records[0]);
        var result = store.Merge(imported);
        Assert(result.Added == 1 && result.Duplicates == 1 && store.Load().Count == 2, "Store merge must save the merged history.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}

static void TestCustomChantsAndVoice()
{
    Texts.Apply(AppLanguage.Russian);
    Assert(ChantBook.Normalize("  Ай — ДАА ") == "ай-даа" && ChantBook.Normalize("ба ки_те-кут") == "ба-ки-те-кут" && ChantBook.Normalize(null) == "",
        "Chant must be normalized to syllables joined by hyphens.");
    Assert(ChantBook.Validate('А', "ай-даа") is null, "Two syllables fit А (.-).");
    Assert(ChantBook.Validate('А', "ай-даа-даа") is { } error && error.Contains("слогов: 3") && error.Contains(".-"), "Wrong syllable count must be explained.");
    Assert(ChantBook.Validate('А', "   ") is not null && ChantBook.Validate('#', "а") is not null, "Empty chant and unknown symbol are rejected.");
    Assert(ChantBook.Validate('Е', new string('а', ChantBook.MaxLength + 1)) is not null, "Too long chant is rejected.");
    Assert(ChantBook.Pattern(".-") == "ти-таа", "Pattern must show short and long syllables.");

    var json = ChantBook.Serialize(new Dictionary<char, string> { ['Б'] = "бааа-ки-те-кут", ['А'] = "ать-даа" });
    Assert(json.Contains("\"А\": \"ать-даа\"") && json.IndexOf("\"А\"", StringComparison.Ordinal) < json.IndexOf("\"Б\"", StringComparison.Ordinal),
        "Chants JSON must be readable and sorted: " + json);
    var read = ChantBook.Deserialize("{\"а\":\"ать-даа\",\"Б\":\"не-то\",\"#\":\"x\",\"АБ\":\"x-y\"}");
    Assert(read.Count == 1 && read['А'] == "ать-даа", "Deserialize must keep only valid chants and upper-case keys.");
    Assert(ChantBook.Deserialize("не json").Count == 0 && ChantBook.Deserialize(null).Count == 0, "Broken JSON gives no chants.");
    var merged = ChantBook.Merge(new Dictionary<char, string> { ['А'] = "ать-даа", ['Т'] = "таак" }, new Dictionary<char, string> { ['А'] = "ай-даа" });
    Assert(merged.Count == 2 && merged['А'] == "ай-даа", "Imported chants win.");

    // Каталог: свой напев подставляется в карточку, встроенный остаётся доступен
    try
    {
        LearningCatalog.SetCustomChants(new Dictionary<char, string> { ['А'] = "ать-даа" });
        var card = LearningCatalog.Find('а')!;
        Assert(card.Chant == "ать-даа" && card.IsCustom && card.CategoryLabel.EndsWith("свой напев", StringComparison.Ordinal), "Custom chant must replace the card chant.");
        Assert(LearningCatalog.GetItems(0).First(item => item.Symbol == 'А').IsCustom && !LearningCatalog.GetItems(0).First(item => item.Symbol == 'Б').IsCustom,
            "Only the edited card is custom.");
        Assert(LearningCatalog.BuiltInChant('А') == "ай-даа", "Built-in chant must stay available.");
        Texts.Apply(AppLanguage.English);
        Assert(card.CategoryLabel == "Russian letters · custom chant", "Card category must follow the language: " + card.CategoryLabel);
        Texts.Apply(AppLanguage.Russian);
    }
    finally
    {
        LearningCatalog.SetCustomChants(new Dictionary<char, string>());
    }

    Assert(LearningCatalog.Find('А')!.Chant == "ай-даа", "Reset must bring the built-in chant back.");

    var directory = Path.Combine(Path.GetTempPath(), "morse-chants-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ChantStore(Path.Combine(directory, "chants.json"));
        Assert(store.Load().Count == 0, "No file — no chants.");
        Assert(store.Set('а', "Ать — Даа")['А'] == "ать-даа" && new ChantStore(Path.Combine(directory, "chants.json")).Load()['А'] == "ать-даа", "Set must normalize and persist.");
        var rejected = false;
        try { store.Set('А', "раз-два-три"); } catch (ArgumentException) { rejected = true; }
        Assert(rejected && store.Load()['А'] == "ать-даа", "Invalid chant must be rejected without changing the file.");
        Assert(store.Merge(new Dictionary<char, string> { ['Т'] = "таак" }).Count == 2, "Merge must add chants.");
        Assert(store.Set('А', "").Count == 1 && !store.Load().ContainsKey('А'), "Empty chant must restore the built-in one.");
        store.Set('Т', null);
        Assert(!File.Exists(Path.Combine(directory, "chants.json")), "The file is removed when no chants are left.");

        // Свой голос: имена code_XXXX, поиск по расширениям
        var voice = Path.Combine(directory, CustomVoice.FolderName);
        Directory.CreateDirectory(voice);
        File.WriteAllBytes(Path.Combine(voice, "code_01.m4a"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(voice, "readme.txt"), new byte[] { 1 });
        Assert(CustomVoice.IsClipFileName("code_01.wav") && CustomVoice.IsClipFileName("CODE_1110.M4A") && !CustomVoice.IsClipFileName("code_2.wav") && !CustomVoice.IsClipFileName("a.wav"),
            "Clip file names must look like code_XXXX with 0 and 1.");
        Assert(CustomVoice.Count(voice) == 1, "Only clip files are counted.");
        Assert(CustomVoice.Find(voice, 'А', new[] { ".wav" }) is null && CustomVoice.Find(voice, 'А', new[] { ".m4a", ".wav" })!.EndsWith("code_01.m4a", StringComparison.Ordinal),
            "Find must respect the platform extensions.");
        Assert(CustomVoice.Find(voice, 'Б', new[] { ".m4a" }) is null && CustomVoice.Find(Path.Combine(directory, "none"), 'А', new[] { ".m4a" }) is null, "Missing clips are null.");
        Assert(CustomVoice.ExampleFileName('А') == "code_01.wav" && CustomVoice.Describe(0).Contains("не добавлен") && CustomVoice.Describe(3).Contains("3"), "Voice hints are wrong.");
    }
    finally
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }

    // Профили переносят свои напевы; старый файл без напевов читается
    var withChants = ProfileTransfer.Export(new[] { new TrainingProfile { Name = "Основной" } }, new Dictionary<char, string> { ['А'] = "ать-даа" });
    var package = ProfileTransfer.ImportPackage(withChants);
    Assert(package.Profiles.Count == 1 && package.Chants.Count == 1 && package.Chants['А'] == "ать-даа", "Profiles export must carry the chants.");
    var withoutChants = ProfileTransfer.Export(new[] { new TrainingProfile { Name = "Основной" } });
    Assert(!withoutChants.Contains("Chants") && ProfileTransfer.ImportPackage(withoutChants).Chants.Count == 0, "No chants — no field.");
}

static void TestPracticeNudge()
{
    Texts.Apply(AppLanguage.Russian);
    var today = new DateOnly(2026, 9, 23);
    var noon = today.ToDateTime(new TimeOnly(12, 0));
    Assert(PracticeNudge.DaysSinceLastPractice(Array.Empty<TrainingRecord>(), today) is null && PracticeNudge.Banner(Array.Empty<TrainingRecord>(), today) is null,
        "A new user gets no banner.");
    var yesterday = new[] { new TrainingRecord { CompletedAt = noon.AddDays(-1) } };
    Assert(PracticeNudge.DaysSinceLastPractice(yesterday, today) == 1 && PracticeNudge.Banner(yesterday, today) is null, "One missed day is not nagged about.");
    var longAgo = new[] { new TrainingRecord { CompletedAt = noon.AddDays(-9) }, new TrainingRecord { CompletedAt = noon.AddDays(-5).AddHours(10) } };
    Assert(PracticeNudge.DaysSinceLastPractice(longAgo, today) == 5 && PracticeNudge.Banner(longAgo, today) == "Вы не тренировались 5 дн. Пять минут сегодня сохранят навык.",
        "Banner must count days since the last practice: " + PracticeNudge.Banner(longAgo, today));
    // Тренировка на бумаге не пишет историю: занятие без неё тоже считается
    Assert(PracticeNudge.Banner(longAgo, today, noon.AddDays(-1)) is null && PracticeNudge.DaysSinceLastPractice(Array.Empty<TrainingRecord>(), today, noon) == 0,
        "Practice without history (paper, listening quiz) resets the banner.");
    Assert(PracticeNudge.DaysSinceLastPractice(yesterday, today, noon.AddDays(-7)) == 1, "An older paper practice does not hide newer history.");
    Assert(PracticeNudge.PracticedOn(today, Array.Empty<TrainingRecord>(), noon) && !PracticeNudge.PracticedOn(today, yesterday, noon.AddDays(-2))
           && PracticeNudge.PracticedOn(today.AddDays(-1), yesterday, null), "Practiced today by history or by paper practice.");

    var evening = today.ToDateTime(new TimeOnly(19, 0));
    Assert(!PracticeNudge.IsReminderDue(evening.AddMinutes(-1), 19 * 60, null, false), "Not before the reminder time.");
    Assert(PracticeNudge.IsReminderDue(evening, 19 * 60, null, false) && PracticeNudge.IsReminderDue(evening.AddHours(3), 19 * 60, today.AddDays(-1), false),
        "Due at and after the time when not shown today.");
    Assert(!PracticeNudge.IsReminderDue(evening.AddHours(1), 19 * 60, today, false), "Only once a day.");
    Assert(!PracticeNudge.IsReminderDue(evening.AddHours(1), 19 * 60, null, true), "No reminder after practice today.");

    Assert(PracticeNudge.TimeChoices.Count == 48 && PracticeNudge.TimeChoices[38] == 19 * 60, "Time list must go every 30 minutes.");
    Assert(PracticeNudge.NearestChoiceIndex(19 * 60) == 38 && PracticeNudge.NearestChoiceIndex(19 * 60 + 14) == 38 && PracticeNudge.NearestChoiceIndex(23 * 60 + 50) == 0,
        "Saved time must snap to the nearest list item.");
}

static void TestCourse()
{
    Texts.Apply(AppLanguage.Russian);
    var russian = Course.Steps(AlphabetMode.Russian);
    var latin = Course.Steps(AlphabetMode.Latin);
    Assert(Course.AlphabetFor((int)AlphabetMode.RussianAndLatin) == AlphabetMode.Russian && Course.AlphabetFor((int)AlphabetMode.Latin) == AlphabetMode.Latin,
        "Course runs on the Russian or Latin Koch order.");
    var kochSteps = russian.Where(step => step.Content == ContentMode.Koch).ToArray();
    Assert(kochSteps[0].KochLevel == KochMethod.MinLevel && kochSteps[^1].KochLevel == KochMethod.MaxLevel(AlphabetMode.Russian)
           && kochSteps.Zip(kochSteps.Skip(1)).All(pair => pair.Second.KochLevel - pair.First.KochLevel is > 0 and <= Course.KochStep),
        "Koch steps go from level 2 to the last level by at most 4 symbols.");
    Assert(russian.Select(step => step.Number).SequenceEqual(Enumerable.Range(1, russian.Count)), "Steps are numbered 1…N.");
    Assert(russian[^1].IsExam && russian[^1].CharactersPerMinute == Course.TargetSpeed && russian[^2].Content == ContentMode.Words && russian[^3].CharactersPerMinute == Course.WordsWarmUpSpeed,
        "Course ends with words at 50 and 60 cpm and the exam.");
    Assert(kochSteps[0].Details == "Первые символы: К М" && kochSteps[1].Details == "Новые символы: Р С У А", "Step details list the new symbols: " + kochSteps[1].Details);
    Assert(latin.Count < russian.Count && latin[0].Details == "Первые символы: K M", "Latin course follows the Latin order.");
    Assert(Course.Find(russian, 0) is null && Course.Find(russian, russian.Count + 1) is null && Course.Find(russian, 1) == russian[0], "Find must return null outside the course.");

    // Настройки шага и пометка записи
    var settings = new AppSettings { AlphabetIndex = (int)AlphabetMode.RussianAndLatin, NoisePercent = 40, AutoSpeed = true, ExamPlaybacks = 3 };
    Course.Apply(russian[1], settings);
    Assert(settings.CourseStep == 2 && settings.AlphabetIndex == (int)AlphabetMode.Russian && settings.ContentModeIndex == (int)ContentMode.Koch && settings.KochLevel == 6
           && settings.CharactersPerMinute == 60 && settings.NoisePercent == 0 && !settings.AutoSpeed, "Apply must set the step settings.");
    Assert(Course.StepForRecord(settings, isExam: false) == 2, "A task with the step settings belongs to the step.");
    settings.CharactersPerMinute = 40;
    Assert(Course.StepForRecord(settings, isExam: false) == 0, "A slower task does not count.");
    var exam = new AppSettings();
    Course.Apply(russian[^1], exam);
    Assert(exam.ExamPlaybacks == 1 && exam.ExamTimeLimitMinutes == Course.ExamTimeLimitMinutes, "Exam step sets the exam rules.");
    Assert(Course.StepForRecord(exam, isExam: false) == 0 && Course.StepForRecord(exam, isExam: true) == russian.Count, "The final step counts only as an exam.");
    Assert(Course.StepForRecord(new AppSettings(), isExam: false) == 0, "Outside the course there is no step.");

    // Зачёт по истории
    var at = new DateTime(2026, 9, 23, 10, 0, 0);
    var history = new List<TrainingRecord>
    {
        TrainingStatistics.CreateRecord(at, "Основной", 60, 10, TrainingEvaluator.Evaluate("АААААААААА", "ААААААААББ"), courseStep: 1),
    };
    Assert(!Course.IsPassed(history, russian[0]) && Course.Status(history, russian[0]).Contains("80 %"), "80 % does not pass: " + Course.Status(history, russian[0]));
    history.Add(TrainingStatistics.CreateRecord(at.AddMinutes(5), "Основной", 60, 10, TrainingEvaluator.Evaluate("ААААА", "ААААА"), courseStep: 1));
    Assert(Course.IsPassed(history, russian[0]) && Course.Status(history, russian[0]).StartsWith("Шаг пройден ✓") && Course.PassedCount(history, russian) == 1,
        "100 % passes the step.");
    Assert(history[1].Kind == "Курс, шаг 1" && history[1].HasKind && !new TrainingRecord().HasKind, "History marks course records.");
    var json = System.Text.Json.JsonSerializer.Serialize(history[1]);
    Assert(json.Contains("\"CourseStep\":1") && !json.Contains("HasKind"), "CourseStep is stored, HasKind is not.");
    Assert(HistoryTransfer.Import(HistoryTransfer.Export(history))[1].CourseStep == 1, "History transfer keeps the course step.");
}

static void TestKeyerProgress()
{
    Assert(KeyerProgress.MatchedLength("CQ DE R3ABC", null) == 0 && KeyerProgress.MatchedLength("", "CQ") == 0, "Nothing received — nothing highlighted.");
    Assert(KeyerProgress.MatchedLength("CQ DE R3ABC", "C") == 1 && KeyerProgress.MatchedLength("CQ DE R3ABC", "CQ") == 2, "Received prefix is highlighted.");
    Assert(KeyerProgress.MatchedLength("CQ DE R3ABC", "CQD") == 4, "Spaces between words are skipped: " + KeyerProgress.MatchedLength("CQ DE R3ABC", "CQD"));
    Assert(KeyerProgress.MatchedLength("CQ DE R3ABC", "CQ DE R3ABC") == 11, "A full match covers the whole task.");
    Assert(KeyerProgress.MatchedLength("CQ DE R3ABC", "CQ DX") == 4, "Highlight stops at the first mistake.");
    Assert(KeyerProgress.MatchedLength("МАМА", "MAM") == 3, "Same-code symbols (М/M, А/A) match.");
    Assert(KeyerProgress.MatchedLength("АБ", "АБВГ") == 2, "Extra symbols do not overflow the task.");
}

static void TestEarQuiz()
{
    Assert(EarQuiz.ClampSpeed(3) == EarQuiz.MinSpeed && EarQuiz.ClampSpeed(999) == EarQuiz.MaxSpeed, "quiz speed is clamped to 20–200");
    Assert(EarQuiz.ClampSpeed(47) == 45 && EarQuiz.ClampSpeed(48) == 50 && EarQuiz.ClampSpeed(EarQuiz.DefaultSpeed) == 45, "quiz speed snaps to a step of 5");
    Assert(new AppSettings().QuizSpeed == EarQuiz.DefaultSpeed && new AppSettings().QuizAutoNext, "defaults: 45 cpm, next symbol plays by itself");
    Assert(EarQuiz.NextDelay(false) > EarQuiz.NextDelay(true), "after a mistake the pause is longer to see the right answer");

    foreach (var alphabet in new[] { 0, 1, 2, 3 })
    {
        var pool = LearningCatalog.GetItems(alphabet);
        char? previous = null;
        for (var round = 0; round < 300; round++)
        {
            var question = EarQuiz.Next(pool, previous);
            Assert(question.Answers.Count == EarQuiz.AnswerCount, $"alphabet {alphabet}: four answers");
            Assert(question.Answers.Count(item => item.Symbol == question.Target.Symbol) == 1, $"alphabet {alphabet}: the target is among the answers once");
            Assert(question.Answers.Select(item => item.Code).Distinct().Count() == question.Answers.Count, $"alphabet {alphabet}: answers never share a code (А and A)");
            Assert(question.Target.Symbol != previous, $"alphabet {alphabet}: the same symbol does not repeat twice in a row");
            // Варианты — ближайшие по коду: любой не выбранный символ не ближе самого далёкого из выбранных
            var target = question.Target.Code;
            var chosen = question.Answers.Where(item => item.Symbol != question.Target.Symbol).Select(item => ProblemDrill.EditDistance(target, item.Code)).ToArray();
            var shownCodes = question.Answers.Select(item => item.Code).ToHashSet();
            var rest = pool.Where(item => !shownCodes.Contains(item.Code)).Select(item => ProblemDrill.EditDistance(target, item.Code)).ToArray();
            Assert(rest.Length == 0 || chosen.Max() <= rest.Min(),
                $"alphabet {alphabet}: answers for {question.Target.Symbol} ({target}) are the most similar: {string.Join(' ', question.Answers.Select(a => a.Symbol))}");
            previous = question.Target.Symbol;
        }
    }

    // Пример: к И (··) — только отличающиеся на один элемент (Е, А, Н, С, У…); random → 0 выбирает первый символ набора
    var russian = LearningCatalog.GetItems(0);
    var forI = EarQuiz.Next(russian.OrderBy(item => item.Symbol == 'И' ? 0 : 1).ToArray(), null, _ => 0);
    Assert(forI.Target.Symbol == 'И' && forI.Answers.Where(a => a.Symbol != 'И').All(a => ProblemDrill.EditDistance("..", a.Code) == 1),
        "similar answers for И are one element away: " + string.Join(' ', forI.Answers.Select(a => a.Symbol)));

    // Ошибки: ошибка +1 (не больше 5), верный ответ −1, на нуле символ убирается; путаный символ звучит чаще
    var misses = new Dictionary<string, int>();
    EarQuiz.Record(misses, 'Б', false);
    EarQuiz.Record(misses, 'Б', false);
    EarQuiz.Record(misses, 'Д', false);
    EarQuiz.Record(misses, 'Д', true);
    Assert(misses.Count == 1 && misses["Б"] == 2, "misses: Б twice, Д forgiven after a right answer");
    for (var n = 0; n < 10; n++) EarQuiz.Record(misses, 'Б', false);
    Assert(misses["Б"] == EarQuiz.MaxMisses && EarQuiz.Weight(misses, 'Б') == 11 && EarQuiz.Weight(misses, 'А') == 1, "miss weight is capped");
    Assert(EarQuiz.Frequent(misses).SequenceEqual(new[] { 'Б' }), "frequent symbols list the confused ones");
    var hits = 0;
    for (var n = 0; n < 2000; n++)
    {
        if (EarQuiz.Next(russian, null, null, misses).Target.Symbol == 'Б') hits++;
    }
    // Вес Б = 11 при 31 символе с весом 1: ожидаемо ~26 % вопросов, без ошибок было бы ~3 %
    Assert(hits is > 350 and < 750, $"confused symbol Б is asked much more often: {hits} of 2000");

    var single = LearningCatalog.GetItems(3).Take(1).ToArray();
    var only = EarQuiz.Next(single, single[0].Symbol);
    Assert(only.Target.Symbol == single[0].Symbol && only.Answers.Count == 1, "a one-symbol pool still asks its only symbol");
}

static void TestEasterEgg()
{
    Assert(EasterEgg.Text == "НЕВАДИМКА" && EasterEgg.Speed == 200, "easter egg: НЕВАДИМКА at 200 cpm");
    Assert(EasterEgg.Text.All(symbol => MorseAlphabet.TryGetCode(symbol, out _)), "every easter egg letter has a Morse code");
    var clip = MorseAudioService.Render(EasterEgg.Text, EasterEgg.Speed, 700, 70, 3, 7);
    Assert(clip.Duration > TimeSpan.FromSeconds(1) && clip.Duration < TimeSpan.FromSeconds(4), "easter egg sounds 1–4 s: " + clip.Duration);
}

static void TestGroupCounter()
{
    // Пометки карточки обучения: свой напев и свой голос
    Texts.Apply(AppLanguage.Russian);
    var card = LearningCatalog.GetItems(0)[0];
    Assert(!card.CategoryLabel.Contains("ваш голос"), "no own voice mark by default");
    Assert((card with { HasOwnVoice = true }).CategoryLabel.EndsWith(" · ваш голос", StringComparison.Ordinal), "own voice mark in the card label");

    // Без сигнала старта первая группа начинается сразу; моменты групп растут, последняя — до конца звука
    var clip = MorseAudioService.Render("АБВГД ЕЖЗИК ЛМНОП", 60, 700, 70, 3, 7);
    Assert(clip.GroupCount == 3 && clip.GroupStarts![0] == TimeSpan.Zero, "three groups, the first starts at once");
    Assert(clip.GroupStarts[1] > clip.GroupStarts[0] && clip.GroupStarts[2] > clip.GroupStarts[1] && clip.GroupStarts[2] < clip.Duration, "group starts grow inside the clip");
    Assert(clip.GroupAt(TimeSpan.Zero) == 1 && clip.GroupAt(clip.GroupStarts[1]) == 2 && clip.GroupAt(clip.Duration) == 3, "group index follows the time");
    // С Ж Ж Ж: пока звучит сигнал старта — группа 0, группы считаются только в задании
    var withStart = MorseAudioService.Render("АБВГД ЕЖЗИК", 60, 700, 70, 3, 7, playStartSignal: true);
    Assert(withStart.GroupCount == 2 && withStart.GroupStarts![0] > TimeSpan.FromSeconds(2) && withStart.GroupAt(TimeSpan.FromSeconds(1)) == 0,
        "start signal is not a group: " + withStart.GroupStarts[0]);
    // Лишние пробелы и слова — те же группы
    Assert(MorseAudioService.Render("  ДОМ   ЛЕС ", 60, 700, 70, 3, 7).GroupCount == 2, "extra spaces do not add groups");
}

static void TestCourseBook()
{
    foreach (var language in new[] { AppLanguage.Russian, AppLanguage.English })
    {
        Texts.Apply(language);
        var pages = CourseBook.Pages(AlphabetMode.Russian);
        Assert(pages.Count == 5 && pages.All(page => page.Title.Length > 0 && page.Paragraphs.Count > 0 && page.Paragraphs.All(p => p.Length > 0)),
            $"{language}: five filled pages");
        var steps = Course.Steps(AlphabetMode.Russian);
        var path = pages[3].Paragraphs;
        Assert(path.Count == steps.Count && path[0] == "1. К М" && path[1] == "2. + Р С У А", $"{language}: the path lists every course step: " + string.Join(" | ", path.Take(3)));
        Assert(pages[1].Paragraphs.Any(p => p.Contains("К М")), $"{language}: the Koch page names the first characters");
    }

    Texts.Apply(AppLanguage.Russian);
    var latin = CourseBook.Pages(AlphabetMode.Latin);
    Assert(latin[3].Paragraphs[0] == "1. K M" && latin[3].Paragraphs.Count == Course.Steps(AlphabetMode.Latin).Count, "latin course book follows the latin course");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
