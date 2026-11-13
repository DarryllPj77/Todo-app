#if IOS || MACCATALYST
using UserNotifications;

namespace TodoApp.Maui.Services;

public sealed class AppleReminderPlatform : IReminderPlatform
{
    private static readonly ForegroundDelegate foreground = new();
    private UNUserNotificationCenter Center
    {
        get { var center = UNUserNotificationCenter.Current; center.Delegate = foreground; return center; }
    }
    public bool SupportsScheduling => true;
    public string DeliveryDescription => "Apple: OS-scheduled local alerts; delivery depends on notification/Focus settings. Only the nearest 32 are scheduled.";
    public async Task<ReminderPermission> PermissionAsync(bool request)
    {
        var center = Center;
        if (request) await center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
        var settings = await center.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus is UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional
            ? ReminderPermission.Allowed : ReminderPermission.Denied;
    }
    private static string Key(int id) => "todo-reminder-" + id;
    public async Task ScheduleAsync(int id, DateTimeOffset when)
    {
        Center.RemoveDeliveredNotifications([Key(id)]);
        var content = new UNMutableNotificationContent { Title = "Todo reminder", Body = "A task reminder is due. Open Todo to review it.", Sound = UNNotificationSound.Default };
        var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(Math.Max(1, (when - DateTimeOffset.UtcNow).TotalSeconds), false);
        await Center.AddNotificationRequestAsync(UNNotificationRequest.FromIdentifier(Key(id), content, trigger));
    }
    public async Task ShowAsync(int id)
    {
        Center.RemoveDeliveredNotifications([Key(id)]);
        var content = new UNMutableNotificationContent { Title = "Todo reminder", Body = "A task reminder is due. Open Todo to review it.", Sound = UNNotificationSound.Default };
        await Center.AddNotificationRequestAsync(UNNotificationRequest.FromIdentifier(Key(id), content, null));
    }
    public Task CancelAsync(int id)
    {
        Center.RemovePendingNotificationRequests([Key(id)]);
        Center.RemoveDeliveredNotifications([Key(id)]);
        return Task.CompletedTask;
    }
    private sealed class ForegroundDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) => completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.Sound);
    }
}
#endif
