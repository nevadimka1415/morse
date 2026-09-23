using System.Globalization;
using System.Text;
using MorseTrainer.Localization;

namespace MorseTrainer.Domain;

/// <summary>Итог экзамена: задание, ответ, оценка, время и параметры.</summary>
public sealed record ExamResult(
    string Task,
    string Answer,
    EvaluationResult Evaluation,
    DateTime StartedAt,
    DateTime FinishedAt,
    int CharactersPerMinute,
    int GroupCount,
    string ProfileName)
{
    public TimeSpan Duration => FinishedAt - StartedAt;

    /// <summary>Сколько прослушиваний разрешали правила и сколько использовано.</summary>
    public int MaxPlaybacks { get; init; } = 1;

    public int PlaybacksUsed { get; init; }

    /// <summary>Лимит времени; null — без лимита.</summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>Ответ проверен автоматически, потому что вышло время.</summary>
    public bool TimedOut { get; init; }
}

/// <summary>
/// Экзамен: задание фиксировано, прослушиваний не больше MaxPlaybacks (1–3), ответ скрыт, время идёт от старта
/// до проверки; при лимите времени по его истечении ответ проверяется автоматически.
/// Логика общая для Windows и телефона; интерфейс только показывает состояние.
/// </summary>
public sealed class ExamSession
{
    public const int MinPlaybacks = 1;
    public const int MaxAllowedPlaybacks = 3;

    /// <summary>Варианты лимита времени в минутах для списков в настройках; 0 — без лимита.</summary>
    public static readonly IReadOnlyList<int> TimeLimitChoices = new[] { 0, 1, 2, 3, 5, 10, 15, 20, 30 };

    public ExamSession(string task, int charactersPerMinute, int groupCount, string profileName, DateTime startedAt,
        int maxPlaybacks = MinPlaybacks, int timeLimitMinutes = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task);
        Task = task;
        CharactersPerMinute = charactersPerMinute;
        GroupCount = groupCount;
        ProfileName = string.IsNullOrWhiteSpace(profileName) ? "Основной" : profileName.Trim();
        StartedAt = startedAt;
        MaxPlaybacks = ClampPlaybacks(maxPlaybacks);
        var minutes = ClampTimeLimit(timeLimitMinutes);
        TimeLimit = minutes > 0 ? TimeSpan.FromMinutes(minutes) : null;
    }

    public static int ClampPlaybacks(int playbacks) => Math.Clamp(playbacks, MinPlaybacks, MaxAllowedPlaybacks);

    public static int ClampTimeLimit(int minutes) => Math.Clamp(minutes, 0, TimeLimitChoices[^1]);

    public int MaxPlaybacks { get; }

    public TimeSpan? TimeLimit { get; }

    public string Task { get; }

    public int CharactersPerMinute { get; }

    public int GroupCount { get; }

    public string ProfileName { get; }

    public DateTime StartedAt { get; }

    public int PlaybacksUsed { get; private set; }

    public bool CanPlay => !IsFinished && PlaybacksUsed < MaxPlaybacks;

    public bool IsFinished { get; private set; }

    /// <summary>Отмечает прослушивание; сверх MaxPlaybacks — исключение.</summary>
    public void RegisterPlayback()
    {
        if (!CanPlay)
        {
            throw new InvalidOperationException("No playbacks left in this exam.");
        }

        PlaybacksUsed++;
    }

    public TimeSpan Elapsed(DateTime now) => now < StartedAt ? TimeSpan.Zero : now - StartedAt;

    /// <summary>Сколько осталось до лимита; null — лимита нет.</summary>
    public TimeSpan? Remaining(DateTime now) => TimeLimit is null
        ? null
        : TimeLimit.Value > Elapsed(now) ? TimeLimit.Value - Elapsed(now) : TimeSpan.Zero;

    public bool IsTimeUp(DateTime now) => TimeLimit is not null && Elapsed(now) >= TimeLimit.Value;

    /// <summary>Строка состояния для экрана: прослушивания и время (прошло или осталось).</summary>
    public string Status(DateTime now) => TimeLimit is null
        ? Texts.F("Экзамен · прослушано {0} из {1} · {2}", PlaybacksUsed, MaxPlaybacks, ExamReport.FormatDuration(Elapsed(now)))
        : Texts.F("Экзамен · прослушано {0} из {1} · осталось {2}", PlaybacksUsed, MaxPlaybacks, ExamReport.FormatDuration(Remaining(now)!.Value));

    public ExamResult Finish(string answer, DateTime finishedAt)
    {
        if (IsFinished)
        {
            throw new InvalidOperationException("The exam is already finished.");
        }

        IsFinished = true;
        var evaluation = TrainingEvaluator.Evaluate(Task, answer ?? string.Empty);
        // Проверка после лимита (например, приложение было свёрнуто) — экзамен закончился ровно на лимите
        var timedOut = IsTimeUp(finishedAt);
        var end = timedOut ? StartedAt + TimeLimit!.Value : finishedAt;
        return new ExamResult(Task, (answer ?? string.Empty).Trim(), evaluation, StartedAt, end, CharactersPerMinute, GroupCount, ProfileName)
        {
            MaxPlaybacks = MaxPlaybacks,
            PlaybacksUsed = PlaybacksUsed,
            TimeLimit = TimeLimit,
            TimedOut = timedOut
        };
    }
}

/// <summary>Протокол экзамена: короткая строка для экрана и полный текст для файла или «Поделиться».</summary>
public static class ExamReport
{
    public static string FormatDuration(TimeSpan duration)
    {
        var total = (int)Math.Max(0, Math.Round(duration.TotalSeconds));
        return $"{total / 60}:{total % 60:00}";
    }

    /// <summary>Ошибки, сгруппированные по ожидавшемуся символу (лишние символы ответа не считаются).</summary>
    public static IReadOnlyList<ProblemSymbolCount> MistakesBySymbol(EvaluationResult evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return evaluation.Mistakes
            .Where(mistake => mistake.Expected != '∅')
            .GroupBy(mistake => mistake.Expected)
            .Select(group => new ProblemSymbolCount(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Symbol)
            .ToArray();
    }

    public static string Summary(ExamResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var evaluation = result.Evaluation;
        var mistakes = MistakesBySymbol(evaluation);
        var symbols = mistakes.Count == 0
            ? Texts.T("Ошибок нет")
            : Texts.F("Ошибки по символам: {0}", string.Join("  ", mistakes.Select(item => $"{item.Symbol} ×{item.Count}")));
        var timedOut = result.TimedOut ? "\n" + Texts.T("Время вышло: ответ проверен автоматически") : string.Empty;
        return Texts.F("Экзамен: точность {0:0.#}% ({1} из {2}), время {3}", evaluation.AccuracyPercent, evaluation.CorrectCount,
                   evaluation.TotalCount, FormatDuration(result.Duration)) + "\n" + symbols + timedOut;
    }

    /// <summary>Правила экзамена одной строкой: прослушивания и лимит времени.</summary>
    public static string Rules(int maxPlaybacks, TimeSpan? timeLimit) => timeLimit is null
        ? Texts.F("прослушиваний: {0} · без лимита времени", maxPlaybacks)
        : Texts.F("прослушиваний: {0} · лимит {1} мин", maxPlaybacks, (int)timeLimit.Value.TotalMinutes);

    public static string Format(ExamResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var evaluation = result.Evaluation;
        var mistakes = MistakesBySymbol(evaluation);
        var positions = evaluation.Mistakes
            .Select(mistake => $"{mistake.Position}: {mistake.Expected}→{mistake.Actual?.ToString() ?? "∅"}");
        var builder = new StringBuilder()
            .AppendLine(Texts.T("Протокол экзамена Morse Trainer"))
            .AppendLine(Texts.F("Дата: {0:dd.MM.yyyy HH:mm}", result.FinishedAt))
            .AppendLine(Texts.F("Профиль: {0}", result.ProfileName))
            .AppendLine(Texts.F("Скорость: {0} знаков/мин", result.CharactersPerMinute))
            .AppendLine(Texts.F("Групп: {0}", result.GroupCount))
            .AppendLine(Texts.F("Время: {0}", FormatDuration(result.Duration)))
            .AppendLine(Texts.F("Прослушиваний: {0} из {1}", result.PlaybacksUsed, result.MaxPlaybacks))
            .AppendLine(result.TimeLimit is null ? Texts.T("Лимит времени: нет") : Texts.F("Лимит времени: {0} мин", (int)result.TimeLimit.Value.TotalMinutes))
            .AppendLine(Texts.F("Точность: {0:0.#}% ({1} из {2})", evaluation.AccuracyPercent, evaluation.CorrectCount, evaluation.TotalCount));
        if (result.TimedOut)
        {
            builder.AppendLine(Texts.T("Время вышло: ответ проверен автоматически"));
        }

        if (mistakes.Count == 0)
        {
            builder.AppendLine(Texts.T("Ошибок нет"));
        }
        else
        {
            builder.AppendLine(Texts.F("Ошибки по символам: {0}", string.Join("  ", mistakes.Select(item => $"{item.Symbol} ×{item.Count}"))));
            builder.AppendLine(Texts.F("Позиции ошибок: {0}", string.Join(", ", positions)));
        }

        return builder
            .AppendLine()
            .AppendLine(Texts.T("Задание:"))
            .AppendLine(result.Task)
            .AppendLine(Texts.T("Ответ:"))
            .AppendLine(result.Answer.Length == 0 ? "—" : result.Answer)
            .ToString();
    }
}
