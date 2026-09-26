using MorseTrainer.Localization;
using MorseTrainer.Models;

namespace MorseTrainer.Domain;

/// <summary>Шаг курса: какие настройки ставит кнопка «Начать шаг» и что нужно для зачёта.</summary>
public sealed record CourseStep(
    int Number,
    string Title,
    string Details,
    ContentMode Content,
    AlphabetMode Alphabet,
    int KochLevel,
    int CharactersPerMinute,
    int GroupCount,
    int CharacterGapUnits,
    int GroupGapUnits,
    bool IsExam);

/// <summary>Пункт списка «Выбрать шаг…»: номер шага, подпись («✓ 3. + П Т Л О») и пройден ли он.</summary>
public sealed record CourseStepChoice(int Number, string Label, bool Passed);

/// <summary>Отметка шага в полосе прогресса курса: пройден, текущий или впереди.</summary>
public enum CourseMark
{
    Ahead,
    Current,
    Passed
}

/// <summary>
/// Курс «С нуля до 60 зн/мин»: метод Коха по 4 новых символа на шаг (скорость знака сразу 60, в начале паузы
/// растянуты), затем слова на 50 и 60 зн/мин и итоговый экзамен. Шаг засчитан, когда в истории есть задание
/// этого шага с точностью от 90 %.
/// </summary>
public static class Course
{
    public const double PassAccuracy = 90;
    public const int TargetSpeed = 60;
    public const int WordsWarmUpSpeed = 50;
    public const int KochStep = 4;
    public const int ExamTimeLimitMinutes = 10;

    /// <summary>Курс идёт по русскому или латинскому ряду Коха: «Русский и латинский» считается русским.</summary>
    public static AlphabetMode AlphabetFor(int alphabetIndex) =>
        alphabetIndex == (int)AlphabetMode.Latin ? AlphabetMode.Latin : AlphabetMode.Russian;

    public static IReadOnlyList<CourseStep> Steps(AlphabetMode alphabet)
    {
        alphabet = alphabet == AlphabetMode.Latin ? AlphabetMode.Latin : AlphabetMode.Russian;
        var max = KochMethod.MaxLevel(alphabet);
        var levels = new List<int>();
        for (var level = KochMethod.MinLevel; level < max; level += KochStep)
        {
            levels.Add(level);
        }

        levels.Add(max);
        var steps = new List<CourseStep>();
        var previous = 0;
        foreach (var level in levels)
        {
            var added = string.Join(' ', KochMethod.Pool(alphabet, level).Skip(previous));
            // Первые уровни — с растянутыми паузами: знак звучит на скорости 60, а время подумать есть
            var wide = level <= 14;
            steps.Add(new CourseStep(steps.Count + 1,
                Texts.F("Метод Коха, уровень {0}", level),
                previous == 0 ? Texts.F("Первые символы: {0}", added) : Texts.F("Новые символы: {0}", added),
                ContentMode.Koch, alphabet, level, TargetSpeed, 10, wide ? 5 : 4, wide ? 12 : 9, false));
            previous = level;
        }

        steps.Add(new CourseStep(steps.Count + 1, Texts.F("Слова на {0} знаков в минуту", WordsWarmUpSpeed),
            Texts.T("Целые слова вместо групп: ухо учится узнавать слово, а не отдельные знаки."),
            ContentMode.Words, alphabet, max, WordsWarmUpSpeed, 10, 3, 7, false));
        steps.Add(new CourseStep(steps.Count + 1, Texts.F("Слова на {0} знаков в минуту", TargetSpeed),
            Texts.T("Та же работа на целевой скорости и с обычными паузами."),
            ContentMode.Words, alphabet, max, TargetSpeed, 10, 3, 7, false));
        steps.Add(new CourseStep(steps.Count + 1, Texts.F("Экзамен на {0} знаков в минуту", TargetSpeed),
            Texts.F("Буквы и цифры, 10 групп, одно прослушивание, лимит {0} минут. Точность от 90 % — курс пройден.", ExamTimeLimitMinutes),
            ContentMode.LettersAndDigits, alphabet, max, TargetSpeed, 10, 3, 7, true));
        return steps;
    }

    /// <summary>Шаг по номеру (1…); null — курс не начат или номер вне курса.</summary>
    public static CourseStep? Find(IReadOnlyList<CourseStep> steps, int number) =>
        number >= 1 && number <= steps.Count ? steps[number - 1] : null;

    /// <summary>Ставит настройки шага: алфавит, состав, скорость, паузы; помехи и авто-скорость выключаются.</summary>
    public static void Apply(CourseStep step, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(settings);
        settings.CourseStep = step.Number;
        settings.AlphabetIndex = (int)step.Alphabet;
        settings.ContentModeIndex = (int)step.Content;
        settings.KochLevel = step.KochLevel;
        settings.CharactersPerMinute = step.CharactersPerMinute;
        settings.GroupCount = step.GroupCount;
        settings.CharacterGapUnits = step.CharacterGapUnits;
        settings.GroupGapUnits = step.GroupGapUnits;
        settings.PlayStartSignal = true;
        settings.NoisePercent = 0;
        settings.QsbPercent = 0;
        settings.DriftHz = 0;
        settings.AutoSpeed = false;
        if (step.IsExam)
        {
            settings.ExamPlaybacks = 1;
            settings.ExamTimeLimitMinutes = ExamTimeLimitMinutes;
        }
    }

    /// <summary>Задание сделано по настройкам шага (скорость не ниже, уровень Коха не ниже, экзамен для итогового шага).</summary>
    public static bool Matches(CourseStep step, AppSettings taskSettings, bool isExam)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(taskSettings);
        return AlphabetFor(taskSettings.AlphabetIndex) == step.Alphabet
               && taskSettings.AlphabetIndex != (int)AlphabetMode.RussianAndLatin
               && taskSettings.ContentModeIndex == (int)step.Content
               && taskSettings.CharactersPerMinute >= step.CharactersPerMinute
               && (step.Content != ContentMode.Koch || taskSettings.KochLevel >= step.KochLevel)
               && (!step.IsExam || isExam);
    }

    /// <summary>Номер шага для записи истории: текущий шаг курса, если задание сделано по его настройкам; иначе 0.</summary>
    public static int StepForRecord(AppSettings taskSettings, bool isExam)
    {
        ArgumentNullException.ThrowIfNull(taskSettings);
        var step = Find(Steps(AlphabetFor(taskSettings.AlphabetIndex)), taskSettings.CourseStep);
        return step is not null && Matches(step, taskSettings, isExam) ? step.Number : 0;
    }

    public static double? BestAccuracy(IReadOnlyList<TrainingRecord> history, CourseStep step)
    {
        ArgumentNullException.ThrowIfNull(history);
        var records = history.Where(item => item.CourseStep == step.Number && (!step.IsExam || item.IsExam)).ToArray();
        return records.Length == 0 ? null : records.Max(item => item.AccuracyPercent);
    }

    public static bool IsPassed(IReadOnlyList<TrainingRecord> history, CourseStep step) =>
        BestAccuracy(history, step) is >= PassAccuracy;

    public static int PassedCount(IReadOnlyList<TrainingRecord> history, IReadOnlyList<CourseStep> steps) =>
        steps.Count(step => IsPassed(history, step));

    /// <summary>«1. К М», «2. + Р С У А», … затем шаги со словами и экзамен — для книжки и списка «Выбрать шаг…».</summary>
    public static IReadOnlyList<string> StepLines(IReadOnlyList<CourseStep> steps)
    {
        var lines = new List<string>();
        var previous = 0;
        foreach (var step in steps)
        {
            if (step.Content == ContentMode.Koch)
            {
                var added = string.Join(' ', KochMethod.Pool(step.Alphabet, step.KochLevel).Skip(previous));
                lines.Add(previous == 0 ? $"{step.Number}. {added}" : $"{step.Number}. + {added}");
                previous = step.KochLevel;
            }
            else
            {
                lines.Add($"{step.Number}. {step.Title}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Список для «Выбрать шаг…» (телефон и Windows): все шаги, пройденные по истории отмечены ✓. После случайного
    /// сброса курса видно, где остановились, и можно продолжить с нужного шага, а не проходить всё с первого.
    /// </summary>
    public static IReadOnlyList<CourseStepChoice> StepChoices(IReadOnlyList<CourseStep> steps, IReadOnlyList<TrainingRecord> history)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(history);
        var lines = StepLines(steps);
        return steps.Select((step, index) =>
        {
            var passed = IsPassed(history, step);
            return new CourseStepChoice(step.Number, (passed ? "✓ " : "") + lines[index], passed);
        }).ToArray();
    }

    /// <summary>Полоса прогресса курса: по отметке на шаг — пройден (по истории), текущий (current) или впереди.</summary>
    public static IReadOnlyList<CourseMark> Marks(IReadOnlyList<CourseStep> steps, IReadOnlyList<TrainingRecord> history, int current)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(history);
        return steps.Select(step => IsPassed(history, step) ? CourseMark.Passed : step.Number == current ? CourseMark.Current : CourseMark.Ahead).ToArray();
    }

    /// <summary>Строка состояния шага: зачтён или какой лучший результат.</summary>
    public static string Status(IReadOnlyList<TrainingRecord> history, CourseStep step)
    {
        var best = BestAccuracy(history, step);
        return best switch
        {
            null => Texts.F("Нужна точность от {0} % в задании этого шага.", PassAccuracy),
            >= PassAccuracy => Texts.F("Шаг пройден ✓ (лучший результат {0:0.#} %).", best.Value),
            _ => Texts.F("Лучший результат {0:0.#} % — нужно от {1} %.", best.Value, PassAccuracy)
        };
    }
}
