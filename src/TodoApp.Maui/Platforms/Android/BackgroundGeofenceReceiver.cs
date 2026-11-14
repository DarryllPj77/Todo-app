using Android.App;
using Android.Content;
using Android.Gms.Location;
using AndroidX.Core.Location;
using TodoApp.Domain.Location;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.Platforms.Android;

[BroadcastReceiver(Name = "com.companyname.todoapp.maui.BackgroundGeofenceReceiver", Enabled = true, Exported = false)]
public sealed class BackgroundGeofenceReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (WorkspacePreview.Enabled || intent?.Action != AndroidBackgroundGeofencePlatform.Action) return;
        var pending = GoAsync();
        _ = AndroidBackgroundGeofenceHost.ReceiveAsync(intent).WaitAsync(TimeSpan.FromSeconds(8)).ContinueWith(task =>
        { _ = task.Exception; pending?.Finish(); }, TaskScheduler.Default);
    }
}

[BroadcastReceiver(Name = "com.companyname.todoapp.maui.BackgroundGeofenceLifecycleReceiver", Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
public sealed class BackgroundGeofenceLifecycleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action is not (Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced)) return;
        var generation = AndroidBackgroundGeofenceHost.BeginLifecycleInvalidation();
        if (generation is null) return;
        var pending = GoAsync();
        _ = AndroidBackgroundGeofenceHost.FinishLifecycleInvalidationAsync(generation).WaitAsync(TimeSpan.FromSeconds(8)).ContinueWith(task =>
        { _ = task.Exception; pending?.Finish(); }, TaskScheduler.Default);
    }
}

public static class AndroidBackgroundGeofenceHost
{
    private static string DirectoryPath => FileSystem.AppDataDirectory;
    private static BackgroundGeofenceBlocker Blocker() => new(Path.Combine(DirectoryPath, "geofence-control.db3"));
    private static BackgroundGeofenceService Service() => DeviceBackgroundGeofenceHost.Create(DirectoryPath, new LocationEncryptionKeyStore(),
        new AndroidBackgroundGeofencePlatform(), new AndroidReminderPlatform(), Blocker());
    public static long? BeginLifecycleInvalidation()
    {
        if (WorkspacePreview.Enabled || !File.Exists(Path.Combine(DirectoryPath, "geofence-background.db3"))) return null;
        try { return Blocker().Block(); } catch { return null; } // Unreadable control is fail-closed at delivery.
    }
    public static async Task FinishLifecycleInvalidationAsync(long? generation)
    {
        if (generation is null || WorkspacePreview.Enabled) return;
        try { using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(7)); await Service().StopForLifecycleAsync(generation.Value, budget.Token); }
        catch { /* Durable block precedes cleanup. UI Stop can retry; no automatic re-registration. */ }
    }
    public static async Task ReviewResumeAsync()
    {
        if (WorkspacePreview.Enabled || !File.Exists(Path.Combine(DirectoryPath, "geofence-background.db3"))) return;
        try
        {
            if (AndroidGeofencePolicy.Next(await AndroidBackgroundGeofencePlatform.AccessSnapshotAsync()) != AndroidGeofenceAccess.Ready)
                await FinishLifecycleInvalidationAsync(BeginLifecycleInvalidation());
            else { using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(7)); await Service().ReconcileAsync(budget.Token); }
        }
        catch { /* No permission requests or UI from lifecycle reconciliation. */ }
    }
    public static async Task ReceiveAsync(Intent intent)
    {
        if (WorkspacePreview.Enabled || intent.Action != AndroidBackgroundGeofencePlatform.Action) return;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        try
        {
            var data = GeofencingEvent.FromIntent(intent);
            if (data is null) return;
            if (data.HasError || AndroidGeofencePolicy.Next(await AndroidBackgroundGeofencePlatform.AccessSnapshotAsync()) != AndroidGeofenceAccess.Ready)
            { await FinishLifecycleInvalidationAsync(BeginLifecycleInvalidation()); return; }
            var location = data.TriggeringLocation;
            PositionFix? fix = location is null ? null : new(location.Latitude, location.Longitude, location.HasAccuracy ? location.Accuracy : null,
                DateTimeOffset.FromUnixTimeMilliseconds(location.Time), LocationCompat.IsMock(location));
            var ids = data.TriggeringGeofences?.Select(g => g.RequestId).ToArray();
            var reports = AndroidGeofencePolicy.Reports(data.GeofenceTransition, ids!, fix);
            if (reports.Count > 0) await Service().ProcessAsync(() => reports, budget.Token);
        }
        catch { /* No raw coordinates, provider payload or account data in logs. */ }
    }
}
