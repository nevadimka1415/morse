using Android.App;
using Android.Content;
using MorseTrainer.Domain;
using MorseTrainer.Mobile.Services;
using MorseTrainer.Services;

namespace MorseTrainer.Mobile.Platforms.Android;

/// <summary>
/// Напоминание через AlarmManager: повторяющийся будильник будит ReminderReceiver, тот показывает уведомление.
/// После перезагрузки будильник восстанавливает BootReceiver по сохранённым настройкам.
/// </summary>
public sealed class ReminderService : IReminderService
{
    public const string ChannelId = "morse-reminder";
    public const int RequestCode = 1415;
    public const string TitleExtra = "title";
    public const string MessageExtra = "message";

    public async Task<bool> RequestPermissionAsync()
    {
        // Разрешение POST_NOTIFICATIONS появилось в Android 13; раньше уведомления разрешены по умолчанию
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return true;
        }

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        }

        return status == PermissionStatus.Granted;
    }

    public Task ScheduleDailyAsync(TimeSpan time, string title, string message)
    {
        var context = global::Android.App.Application.Context;
        Schedule(context, time, title, message);
        return Task.CompletedTask;
    }

    public void Cancel()
    {
        var context = global::Android.App.Application.Context;
        var alarm = context.GetSystemService(Context.AlarmService) as AlarmManager;
        alarm?.Cancel(CreatePendingIntent(context, string.Empty, string.Empty));
    }

    public static void Schedule(Context context, TimeSpan time, string title, string message)
    {
        EnsureChannel(context);
        var alarm = context.GetSystemService(Context.AlarmService) as AlarmManager;
        if (alarm is null)
        {
            return;
        }

        var pending = CreatePendingIntent(context, title, message);
        var next = ReminderSchedule.NextOccurrence(DateTime.Now, time);
        var triggerAt = new DateTimeOffset(next).ToUnixTimeMilliseconds();
        // Неточный повтор раз в сутки: система может сдвинуть на несколько минут, точный будильник не нужен
        alarm.SetRepeating(AlarmType.RtcWakeup, triggerAt, AlarmManager.IntervalDay, pending);
    }

    private static PendingIntent CreatePendingIntent(Context context, string title, string message)
    {
        var intent = new Intent(context, typeof(ReminderReceiver));
        intent.PutExtra(TitleExtra, title);
        intent.PutExtra(MessageExtra, message);
        return PendingIntent.GetBroadcast(context, RequestCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    public static void EnsureChannel(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        var channel = new NotificationChannel(ChannelId, ReminderTexts.Title, NotificationImportance.Default)
        {
            Description = "Daily practice reminder"
        };
        manager?.CreateNotificationChannel(channel);
    }

    /// <summary>Показывает уведомление; нажатие открывает приложение.</summary>
    public static void ShowNotification(Context context, string title, string message)
    {
        EnsureChannel(context);
        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager is null)
        {
            return;
        }

        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName ?? string.Empty);
        var contentIntent = launch is null
            ? null
            : PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
#pragma warning disable CA1422 // конструктор без канала нужен только для Android 7
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, ChannelId)
            : new Notification.Builder(context);
#pragma warning restore CA1422
        builder.SetContentTitle(title)
            .SetContentText(message)
            .SetSmallIcon(context.ApplicationInfo?.Icon ?? 0)
            .SetAutoCancel(true);
        if (contentIntent is not null)
        {
            builder.SetContentIntent(contentIntent);
        }

        manager.Notify(RequestCode, builder.Build());
    }
}

/// <summary>Срабатывает по будильнику и показывает уведомление.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        var settings = new MobileSettingsService().LoadSettings();
        if (!settings.ReminderEnabled)
        {
            return;
        }

        // Сегодня уже занимались (в том числе на бумаге) — не напоминаем, как на Windows
        var history = new TrainingHistoryStore(MobilePaths.HistoryFile).Load();
        if (PracticeNudge.PracticedOn(DateOnly.FromDateTime(DateTime.Now), history, settings.LastPracticeAt))
        {
            return;
        }

        // Текст считается в момент срабатывания: серия «дней подряд» — на сегодня, а не на день, когда ставили будильник
        var title = intent?.GetStringExtra(ReminderService.TitleExtra);
        ReminderService.ShowNotification(context,
            string.IsNullOrWhiteSpace(title) ? ReminderTexts.Title : title,
            ReminderTexts.Body(settings, history));
    }
}

/// <summary>После перезагрузки будильники сброшены: ставим напоминание заново по настройкам.</summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted)
        {
            return;
        }

        var settings = new MobileSettingsService().LoadSettings();
        if (settings.ReminderEnabled)
        {
            ReminderService.Schedule(context, ReminderSchedule.ToTime(settings.ReminderMinutes), ReminderTexts.Title,
                ReminderTexts.Body(settings, new TrainingHistoryStore(MobilePaths.HistoryFile).Load()));
        }
    }
}
