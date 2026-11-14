using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using global::Windows.ApplicationModel.Background;
using global::Windows.Devices.Geolocation;
using global::Windows.Devices.Geolocation.Geofencing;

namespace TodoApp.Maui.Platforms.Windows;

public sealed class WindowsBackgroundGeofencePlatform(string journalPath) : IBackgroundGeofencePlatform
{
    public const string TaskName = "Todo.BackgroundGeofence.v1";
    public const string Prefix = "todo-background-v1-";
    public static readonly Guid TaskClassId = new("93208D98-A7E6-4E58-AAF3-CDB720D94E77");
    public bool IsSupported { get { try { return !string.IsNullOrWhiteSpace(global::Windows.ApplicationModel.Package.Current.Id.Name); } catch { return false; } } }
    public string Description => IsSupported
        ? "Packaged Windows: one device-local reminder can run for up to 30 minutes after the UI closes, subject to OS background/location policy. No automatic boot recovery."
        : "Unpackaged Windows: background runs require an installed MSIX with background COM activation. Manual checks remain available.";
    public async Task RequestAccessAsync(CancellationToken token)
    {
        if (!IsSupported) throw new InvalidOperationException(Description);
        token.ThrowIfCancellationRequested();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
                throw new InvalidOperationException("Windows location access is denied/unavailable. Allow it in OS settings and explicitly retry.");
            token.ThrowIfCancellationRequested();
            var access = await BackgroundExecutionManager.RequestAccessAsync();
            if (access is BackgroundAccessStatus.Unspecified or BackgroundAccessStatus.Denied or BackgroundAccessStatus.DeniedBySystemPolicy or BackgroundAccessStatus.DeniedByUser)
                throw new InvalidOperationException("Windows background access is denied/unavailable. Change OS policy/settings before explicitly retrying.");
        });
        token.ThrowIfCancellationRequested();
    }
    public Task RegisterAsync(BackgroundGeofenceRun run, CancellationToken token) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        token.ThrowIfCancellationRequested();
        var builder = new Microsoft.Windows.ApplicationModel.Background.BackgroundTaskBuilder { Name = TaskName };
        builder.SetTaskEntryPointClsid(TaskClassId);
        builder.SetTrigger(new LocationTrigger(LocationTriggerType.Geofence)); builder.Register();
        var row = run.Reminder; var spec = new NativeGeofenceSpec(row.Id, row.ArmId, row.Latitude, row.Longitude, row.RadiusMeters);
        var monitor = GeofenceMonitor.Current;
        var center = new BasicGeoposition { Latitude = row.Latitude, Longitude = row.Longitude };
        var duration = run.ExpiresUtc - DateTimeOffset.UtcNow;
        if (duration <= TimeSpan.Zero) throw new InvalidOperationException("Background registration expired before completion.");
        monitor.Geofences.Add(new Geofence(run.EntryId, new Geocircle(center, spec.EntryRadius), MonitoredGeofenceStates.Entered | MonitoredGeofenceStates.Removed,
            false, TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow, duration));
        token.ThrowIfCancellationRequested();
        monitor.Geofences.Add(new Geofence(run.ExitId, new Geocircle(center, spec.ExitRadius), MonitoredGeofenceStates.Exited | MonitoredGeofenceStates.Removed,
            false, TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow, run.ExpiresUtc - DateTimeOffset.UtcNow));
        token.ThrowIfCancellationRequested();
    });
    public Task CleanupAsync()
    {
        if (!IsSupported) return Task.CompletedTask;
        var registrations = BackgroundTaskRegistration.AllTasks.Values.Where(t => t.Name == TaskName).ToArray();
        foreach (var task in registrations) task.Unregister(true);
        // Avoid touching location APIs for installations that never opted in.
        if (registrations.Length == 0 && !File.Exists(journalPath)) return Task.CompletedTask;
        var monitor = GeofenceMonitor.Current;
        foreach (var fence in monitor.Geofences.Where(g => g.Id.StartsWith(Prefix, StringComparison.Ordinal)).ToArray()) monitor.Geofences.Remove(fence);
        return Task.CompletedTask;
    }
    public static IReadOnlyList<BackgroundGeofenceReport> ReadReports() => GeofenceMonitor.Current.ReadReports()
        .Where(r => r.Geofence.Id.StartsWith(Prefix, StringComparison.Ordinal)).Select(r =>
        {
            var coordinate = r.Geoposition.Coordinate;
            return new BackgroundGeofenceReport(r.Geofence.Id, r.NewState switch
            {
                GeofenceState.Entered => NativeGeofenceTransition.Entered,
                GeofenceState.Exited => NativeGeofenceTransition.Exited,
                _ => NativeGeofenceTransition.Removed
            }, new(coordinate.Point.Position.Latitude, coordinate.Point.Position.Longitude, coordinate.Accuracy, coordinate.Timestamp));
        }).ToArray();
}
