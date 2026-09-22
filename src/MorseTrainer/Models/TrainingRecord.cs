using System.Text.Json.Serialization;
using MorseTrainer.Localization;

namespace MorseTrainer.Models;

/// <summary>Одна завершённая тренировка: первая проверка ответа по заданию.</summary>
public sealed class TrainingRecord
{
    public DateTime CompletedAt { get; set; }

    /// <summary>Запись экзамена (одно прослушивание, время) — в истории помечается отдельно.</summary>
    public bool IsExam { get; set; }

    /// <summary>Подпись вида записи для таблиц: «Экзамен» или пусто.</summary>
    [JsonIgnore]
    public string Kind => IsExam ? Texts.T("Экзамен") : string.Empty;
    public string ProfileName { get; set; } = "Основной";
    public int CharactersPerMinute { get; set; }
    public int GroupCount { get; set; }
    public int TotalCount { get; set; }
    public int CorrectCount { get; set; }
    public double AccuracyPercent { get; set; }

    /// <summary>Ожидавшиеся символы, в которых была ошибка, по одному на каждую ошибку.</summary>
    public string ProblemSymbols { get; set; } = string.Empty;
}
