using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.Platforms.Windows;

// The current app is unpackaged. Do not claim durable OS scheduling for it.
// Persisted reminders are evaluated by the app's runtime while it is running.
public sealed class WindowsReminderPlatform : IReminderPlatform
{
    private bool registered;
    public bool SupportsScheduling => false;
    public string DeliveryDescription => "Windows development build: keep the app running. Alerts may be delayed by sleep or system notification settings; no closed-app delivery.";
    private void Register()
    {
        if (registered) return;
        AppNotificationManager.Default.NotificationInvoked += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var window in Application.Current?.Windows ?? [])
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native) native.Activate();
        });
        AppNotificationManager.Default.Register();
        registered = true;
    }
    public Task<ReminderPermission> PermissionAsync(bool request)
    {
        if (!AppNotificationManager.IsSupported()) return Task.FromResult(ReminderPermission.Unavailable);
        Register();
        return Task.FromResult(AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled
            ? ReminderPermission.Allowed : ReminderPermission.Denied);
    }
    public Task ScheduleAsync(int id, DateTimeOffset when) => throw new NotSupportedException("This Windows build schedules while running only.");
    public Task ShowAsync(int id)
    {
        Register();
        var notification = new AppNotificationBuilder().AddText("Todo reminder")
            .AddText("A task reminder is due. Open Todo to review it.").BuildNotification();
        notification.Tag = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        notification.Group = "todo-reminders";
        AppNotificationManager.Default.Show(notification);
        return Task.CompletedTask;
    }
    public async Task CancelAsync(int id)
    {
        Register();
        await AppNotificationManager.Default.RemoveByTagAndGroupAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture), "todo-reminders");
    }
}
