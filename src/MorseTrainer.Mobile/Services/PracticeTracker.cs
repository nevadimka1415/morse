using Microsoft.Maui.Devices;
using MorseTrainer.Domain;
using MorseTrainer.Models;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Services;

/// <summary>
/// Отмечает занятие на телефоне (задание дослушано, ответ «На слух»): день — для серии «дней подряд» в напоминании,
/// время — чтобы сегодня уже не напоминать. На Android приёмник считает серию сам в момент срабатывания, а на iPhone
/// повторяющееся уведомление в момент показа не пересчитать — там текст с серией обновляется здесь, раз в день.
/// </summary>
public sealed class PracticeTracker
{
    private readonly TrainingHistoryStore _historyStore;
    private readonly IReminderService _reminders;
    private DateOnly _rescheduledOn;

    public PracticeTracker(TrainingHistoryStore historyStore, IReminderService reminders)
    {
        _historyStore = historyStore;
        _reminders = reminders;
    }

    /// <summary>Отмечает занятие в настройках; сохраняет их вызывающий.</summary>
    public void Mark(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var now = DateTime.Now;
        settings.LastPracticeAt = now;
        PracticeNudge.MarkPracticeDay(settings.PracticeDays, now);
        var today = DateOnly.FromDateTime(now);
        if (settings.ReminderEnabled && DeviceInfo.Current.Platform == DevicePlatform.iOS && _rescheduledOn != today)
        {
            _rescheduledOn = today;
            _ = RescheduleAsync(settings);
        }
    }

    private async Task RescheduleAsync(AppSettings settings)
    {
        try
        {
            await _reminders.ScheduleDailyAsync(ReminderSchedule.ToTime(settings.ReminderMinutes), ReminderTexts.Title,
                ReminderTexts.Body(settings, _historyStore.Load()));
        }
        catch (Exception)
        {
            // Не вышло — напоминание останется со старым текстом
        }
    }
}
