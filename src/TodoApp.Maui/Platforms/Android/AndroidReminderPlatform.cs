using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using TodoApp.Domain.Reminders;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.Platforms.Android;

public sealed class AndroidReminderPlatform : IReminderPlatform
{
    internal const string Channel = "todoapp.reminders.v1";
    private static Context Context => global::Android.App.Application.Context;
    public bool SupportsScheduling => true;
    public string DeliveryDescription => "Android: inexact OS alarms may be delayed by Doze/battery settings. Reboot restores future alerts; force-stop requires opening the app again.";
    private static NotificationManager Manager => (NotificationManager)Context.GetSystemService(Context.NotificationService)!;
    private static void EnsureChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            Manager.CreateNotificationChannel(new NotificationChannel(Channel, "Task reminders", NotificationImportance.Default));
    }
    public async Task<ReminderPermission> PermissionAsync(bool request)
    {
        EnsureChannel();
        if (request && Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<NotificationPermission>());
        var allowed = NotificationManagerCompat.From(Context).AreNotificationsEnabled();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O && Manager.GetNotificationChannel(Channel)?.Importance == NotificationImportance.None) allowed = false;
        return allowed ? ReminderPermission.Allowed : ReminderPermission.Denied;
    }
    private static Intent AlarmIntent(int id) => new Intent(Context, typeof(ReminderReceiver))
        .SetAction("todoapp.reminder." + id);
    public Task ScheduleAsync(int id, DateTimeOffset when)
    {
        var intent = AlarmIntent(id); intent.PutExtra("id", id); intent.PutExtra("when", when.ToUnixTimeMilliseconds());
        var pending = PendingIntent.GetBroadcast(Context, id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var alarm = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M) alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, when.ToUnixTimeMilliseconds(), pending);
        else alarm.Set(AlarmType.RtcWakeup, when.ToUnixTimeMilliseconds(), pending);
        return Task.CompletedTask;
    }
    public Task ShowAsync(int id)
    {
        EnsureChannel();
        var launch = Context.PackageManager!.GetLaunchIntentForPackage(Context.PackageName!)!;
        launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var open = PendingIntent.GetActivity(Context, id, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var notification = new NotificationCompat.Builder(Context, Channel).SetSmallIcon(Resource.Drawable.reminder_icon)
            .SetContentTitle("Todo reminder").SetContentText("A task reminder is due. Open Todo to review it.")
            .SetContentIntent(open).SetAutoCancel(true).SetVisibility(NotificationCompat.VisibilityPrivate).Build();
        NotificationManagerCompat.From(Context).Notify(id, notification);
        return Task.CompletedTask;
    }
    public Task CancelAsync(int id)
    {
        var pending = PendingIntent.GetBroadcast(Context, id, AlarmIntent(id), PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pending is not null) { ((AlarmManager)Context.GetSystemService(Context.AlarmService)!).Cancel(pending); pending.Cancel(); }
        NotificationManagerCompat.From(Context).Cancel(id);
        return Task.CompletedTask;
    }
    public sealed class NotificationPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu ? [(global::Android.Manifest.Permission.PostNotifications, true)] : [];
    }
}

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent is null) return;
        var pending = GoAsync();
        _ = HandleAsync(intent).ContinueWith(_ => pending?.Finish(), TaskScheduler.Default);
    }
    internal static async Task<bool> EligibleAsync(ReminderRecord row)
    {
        if (!row.Enabled) return false;
        var directory = FileSystem.AppDataDirectory;
        if (row.Scope == "device")
        {
            var task = await new SqliteLocalTaskService(Path.Combine(directory, "todoapp.db3")).GetByIdAsync(row.TaskId);
            return task is not null && !task.IsCompleted && task.DueDate == row.TaskDueDate;
        }
        if (row.Scope != Preferences.Default.Get("todoapp.reminder-active-scope", "")) return false;
        var state = await new SqliteSyncStore(Path.Combine(directory, "sync.db3")).ReadAsync(row.Scope);
        return state.Rows.TryGetValue(row.TaskId, out var cached) && cached.Local is { } data && !data.IsCompleted && data.DueDate == row.TaskDueDate;
    }
    private static async Task HandleAsync(Intent intent)
    {
        try
        {
            var id = intent.GetIntExtra("id", 0); var when = intent.GetLongExtra("when", 0);
            var rows = await new SqliteReminderStore(Path.Combine(FileSystem.AppDataDirectory, "reminders.db3")).ReadAsync();
            var row = rows.SingleOrDefault(r => r.Id == id);
            if (row is null || row.FireAtUtc.ToUnixTimeMilliseconds() != when || !await EligibleAsync(row)) return;
            var platform = new AndroidReminderPlatform();
            if (await platform.PermissionAsync(false) == ReminderPermission.Allowed) await platform.ShowAsync(id);
        }
        catch (Exception) { /* No sensitive payload/logs. Reconcile in the app on next launch. */ }
    }
}

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public sealed class ReminderBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted) return;
        var pending = GoAsync();
        _ = RestoreAsync().ContinueWith(_ => pending?.Finish(), TaskScheduler.Default);
    }
    private static async Task RestoreAsync()
    {
        try
        {
            var platform = new AndroidReminderPlatform();
            if (await platform.PermissionAsync(false) != ReminderPermission.Allowed) return;
            var rows = await new SqliteReminderStore(Path.Combine(FileSystem.AppDataDirectory, "reminders.db3")).ReadAsync();
            var count = 0;
            foreach (var row in rows.Where(r => r.FireAtUtc > DateTimeOffset.UtcNow).OrderBy(r => r.FireAtUtc))
                if (await ReminderReceiver.EligibleAsync(row) && ++count <= 32) await platform.ScheduleAsync(row.Id, row.FireAtUtc);
        }
        catch (Exception) { /* App resume exposes reconciliation failures without boot-time prompts. */ }
    }
}
