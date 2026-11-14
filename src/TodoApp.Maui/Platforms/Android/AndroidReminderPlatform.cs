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
    private static NotificationManager Manager => Context.GetSystemService(Context.NotificationService) as NotificationManager ?? throw new InvalidOperationException("Android notifications are unavailable.");
    private static NotificationManagerCompat Compat => NotificationManagerCompat.From(Context) ?? throw new InvalidOperationException("Android notifications are unavailable.");
    private static PendingIntentFlags Immutable => OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0;
    private static void EnsureChannel()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            Manager.CreateNotificationChannel(new NotificationChannel(Channel, "Task reminders", NotificationImportance.Default));
    }
    public async Task<ReminderPermission> PermissionAsync(bool request)
    {
        EnsureChannel();
        if (request && OperatingSystem.IsAndroidVersionAtLeast(33))
            await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<NotificationPermission>());
        var allowed = Compat.AreNotificationsEnabled();
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && (Manager.GetNotificationChannel(Channel) is not { } channel || channel.Importance == NotificationImportance.None)) allowed = false;
        return allowed ? ReminderPermission.Allowed : ReminderPermission.Denied;
    }
    private static Intent AlarmIntent(int id) => new Intent(Context, typeof(ReminderReceiver))
        .SetAction("todoapp.reminder." + id);
    public Task ScheduleAsync(int id, DateTimeOffset when)
    {
        var intent = AlarmIntent(id); intent.PutExtra("id", id); intent.PutExtra("when", when.ToUnixTimeMilliseconds());
        var pending = PendingIntent.GetBroadcast(Context, id, intent, PendingIntentFlags.UpdateCurrent | Immutable)!;
        var alarm = (AlarmManager)Context.GetSystemService(Context.AlarmService)!;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, when.ToUnixTimeMilliseconds(), pending);
        else alarm.Set(AlarmType.RtcWakeup, when.ToUnixTimeMilliseconds(), pending);
        return Task.CompletedTask;
    }
    public Task ShowAsync(int id)
    {
        EnsureChannel();
        var launch = Context.PackageManager?.GetLaunchIntentForPackage(Context.PackageName!) ?? throw new InvalidOperationException("Android could not find Todo's launcher.");
        launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var open = PendingIntent.GetActivity(Context, id, launch, PendingIntentFlags.UpdateCurrent | Immutable);
        var builder = new NotificationCompat.Builder(Context, Channel);
        builder.SetSmallIcon(Resource.Drawable.reminder_icon); builder.SetContentTitle("Todo reminder");
        builder.SetContentText("A task reminder is due. Open Todo to review it."); builder.SetContentIntent(open);
        builder.SetAutoCancel(true); builder.SetVisibility(NotificationCompat.VisibilityPrivate);
        Compat.Notify(id, builder.Build());
        return Task.CompletedTask;
    }
    public Task CancelAsync(int id)
    {
        var pending = PendingIntent.GetBroadcast(Context, id, AlarmIntent(id), PendingIntentFlags.NoCreate | Immutable);
        if (pending is not null) { ((AlarmManager)Context.GetSystemService(Context.AlarmService)!).Cancel(pending); pending.Cancel(); }
        Compat.Cancel(id);
        return Task.CompletedTask;
    }
    public sealed class NotificationPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(33) ? [(global::Android.Manifest.Permission.PostNotifications, true)] : [];
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
