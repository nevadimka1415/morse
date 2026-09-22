using Foundation;
using MorseTrainer.Mobile.Services;
using UserNotifications;

namespace MorseTrainer.Mobile.Platforms.iOS;

/// <summary>Напоминание через UNUserNotificationCenter: календарный триггер с повтором каждый день.</summary>
public sealed class ReminderService : IReminderService
{
    private const string Identifier = "morse-daily-reminder";

    public Task<bool> RequestPermissionAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        UNUserNotificationCenter.Current.RequestAuthorization(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge,
            (granted, _) => completion.TrySetResult(granted));
        return completion.Task;
    }

    public Task ScheduleDailyAsync(TimeSpan time, string title, string message)
    {
        var center = UNUserNotificationCenter.Current;
        center.RemovePendingNotificationRequests(new[] { Identifier });
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = message,
            Sound = UNNotificationSound.Default
        };
        var components = new NSDateComponents { Hour = time.Hours, Minute = time.Minutes };
        var trigger = UNCalendarNotificationTrigger.CreateTrigger(components, true);
        var request = UNNotificationRequest.FromIdentifier(Identifier, content, trigger);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        center.AddNotificationRequest(request, error =>
        {
            if (error is null)
            {
                completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetException(new InvalidOperationException(error.LocalizedDescription));
            }
        });
        return completion.Task;
    }

    public void Cancel()
    {
        UNUserNotificationCenter.Current.RemovePendingNotificationRequests(new[] { Identifier });
    }
}
