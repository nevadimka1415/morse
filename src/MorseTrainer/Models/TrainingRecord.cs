using System.Text.Json.Serialization;
using MorseTrainer.Localization;

namespace MorseTrainer.Models;

/// <summary>Одна завершённая тренировка: первая проверка ответа по заданию.</summary>
public sealed class TrainingRecord
{
    public DateTime CompletedAt { get; set; }

    /// <summary>Запись экзамена (одно прослушивание, время) — в истории помечается отдельно.</summary>
    public bool IsExam { get; set; }

    /// <summary>Подпись вида записи для таблиц: «Экзамен», «Курс, шаг N» (второй курс — «Курс 2, шаг N») или пусто.</summary>
    [JsonIgnore]
    public string Kind => (IsExam, CourseStep > 0, Domain.Course.CourseOf(CourseStep) == 2) switch
    {
        (true, true, false) => Texts.F("Экзамен · курс, шаг {0}", CourseStep),
        (true, true, true) => Texts.F("Экзамен · курс 2, шаг {0}", Domain.Course.DisplayNumber(CourseStep)),
        (true, false, _) => Texts.T("Экзамен"),
        (false, true, false) => Texts.F("Курс, шаг {0}", CourseStep),
        (false, true, true) => Texts.F("Курс 2, шаг {0}", Domain.Course.DisplayNumber(CourseStep)),
        _ => string.Empty
    };

    [JsonIgnore]
    public bool HasKind => IsExam || CourseStep > 0;
    public string ProfileName { get; set; } = "Основной";
    public int CharactersPerMinute { get; set; }
    public int GroupCount { get; set; }
    public int TotalCount { get; set; }
    public int CorrectCount { get; set; }
    public double AccuracyPercent { get; set; }

    /// <summary>Ожидавшиеся символы, в которых была ошибка, по одному на каждую ошибку.</summary>
    public string ProblemSymbols { get; set; } = string.Empty;

    /// <summary>Сколько секунд заняло задание: от создания до проверки, не больше 30 мин; 0 — запись до версии 2.5.0.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Шаг курса «С нуля до 60 зн/мин», по настройкам которого сделано задание; 0 — вне курса.</summary>
    public int CourseStep { get; set; }
}
