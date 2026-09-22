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
}

/// <summary>
/// Экзамен: задание фиксировано, прослушивание одно, ответ скрыт, время идёт от старта до проверки.
/// Логика общая для Windows и телефона; интерфейс только показывает состояние.
/// </summary>
public sealed class ExamSession
{
    public const int MaxPlaybacks = 1;

    public ExamSession(string task, int charactersPerMinute, int groupCount, string profileName, DateTime startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task);
        Task = task;
        CharactersPerMinute = charactersPerMinute;
        GroupCount = groupCount;
        ProfileName = string.IsNullOrWhiteSpace(profileName) ? "Основной" : profileName.Trim();
        StartedAt = startedAt;
    }

    public string Task { get; }

    public int CharactersPerMinute { get; }

    public int GroupCount { get; }

    public string ProfileName { get; }

    public DateTime StartedAt { get; }

    public int PlaybacksUsed { get; private set; }

    public bool CanPlay => !IsFinished && PlaybacksUsed < MaxPlaybacks;

    public bool IsFinished { get; private set; }

    /// <summary>Отмечает прослушивание; второе прослушивание запрещено.</summary>
    public void RegisterPlayback()
    {
        if (!CanPlay)
        {
            throw new InvalidOperationException("The exam allows a single playback.");
        }

        PlaybacksUsed++;
    }

    public TimeSpan Elapsed(DateTime now) => now < StartedAt ? TimeSpan.Zero : now - StartedAt;

    public ExamResult Finish(string answer, DateTime finishedAt)
    {
        if (IsFinished)
        {
            throw new InvalidOperationException("The exam is already finished.");
        }

        IsFinished = true;
        var evaluation = TrainingEvaluator.Evaluate(Task, answer ?? string.Empty);
        return new ExamResult(Task, (answer ?? string.Empty).Trim(), evaluation, StartedAt, finishedAt, CharactersPerMinute, GroupCount, ProfileName);
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
        return Texts.F("Экзамен: точность {0:0.#}% ({1} из {2}), время {3}", evaluation.AccuracyPercent, evaluation.CorrectCount,
                   evaluation.TotalCount, FormatDuration(result.Duration)) + "\n" + symbols;
    }

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
            .AppendLine(Texts.F("Точность: {0:0.#}% ({1} из {2})", evaluation.AccuracyPercent, evaluation.CorrectCount, evaluation.TotalCount));
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
