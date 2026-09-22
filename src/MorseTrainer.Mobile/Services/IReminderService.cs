namespace MorseTrainer.Mobile.Services;

/// <summary>Локальное ежедневное напоминание о тренировке; реализация своя для Android и iOS.</summary>
public interface IReminderService
{
    /// <summary>Запрашивает разрешение на уведомления; true, если разрешено.</summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>Ставит напоминание каждый день в указанное местное время.</summary>
    Task ScheduleDailyAsync(TimeSpan time, string title, string message);

    void Cancel();
}
