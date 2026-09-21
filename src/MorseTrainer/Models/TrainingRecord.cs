namespace MorseTrainer.Models;

/// <summary>Одна завершённая тренировка: первая проверка ответа по заданию.</summary>
public sealed class TrainingRecord
{
    public DateTime CompletedAt { get; set; }
    public string ProfileName { get; set; } = "Основной";
    public int CharactersPerMinute { get; set; }
    public int GroupCount { get; set; }
    public int TotalCount { get; set; }
    public int CorrectCount { get; set; }
    public double AccuracyPercent { get; set; }

    /// <summary>Ожидавшиеся символы, в которых была ошибка, по одному на каждую ошибку.</summary>
    public string ProblemSymbols { get; set; } = string.Empty;
}
