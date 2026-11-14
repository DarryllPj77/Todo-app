using TodoApp.Domain.Location;
using TodoApp.Maui.Services;
using global::Windows.Devices.Geolocation;
using global::Windows.Devices.Geolocation.Geofencing;

namespace TodoApp.Maui.Platforms.Windows;

public sealed class WindowsGeofencePlatform(string lockPath) : INativeGeofencePlatform
{
    private const string Prefix = "todo-native-v1-";
    private FileStream? lease;
    private GeofenceMonitor? monitor;
    private Func<NativeGeofenceReport, Task>? callback;
    private readonly Dictionary<string, (NativeGeofenceSpec Spec, NativeGeofenceTransition Transition)> identities = new(StringComparer.Ordinal);
    public bool IsSupported
    {
        get { try { return !string.IsNullOrWhiteSpace(global::Windows.ApplicationModel.Package.Current.Id.Name); } catch { return false; } }
    }
    public string Description => IsSupported
        ? "Packaged Windows: opt-in native regions for an active page/window session, up to 30 minutes. No closed-app activation. OS region storage contains saved centers while registered."
        : "Unpackaged Windows: native sessions require an installed MSIX build with location capability. Manual arrival checks remain available. No closed-app monitoring.";
    public async Task StartAsync(IReadOnlyList<NativeGeofenceSpec> places, Func<NativeGeofenceReport, Task> report, CancellationToken token)
    {
        if (!IsSupported) throw new InvalidOperationException(Description);
        if (places.Count is < 1 or > 10 || places.Select(p => p.ReminderId).Distinct().Count() != places.Count)
            throw new ArgumentException("Choose 1-10 distinct native places.");
        foreach (var place in places)
        {
            LocationRules.Validate(new(place.ReminderId, "Native region", place.Latitude, place.Longitude, place.RadiusMeters));
            if (place.ArmId == Guid.Empty) throw new ArgumentException("A native region needs an arm identity.");
        }
        token.ThrowIfCancellationRequested();
        var access = await MainThread.InvokeOnMainThreadAsync(async () => await Geolocator.RequestAccessAsync());
        token.ThrowIfCancellationRequested();
        if (access != GeolocationAccessStatus.Allowed) throw new InvalidOperationException("Windows location access was denied/unavailable. Allow location in OS settings and explicitly retry.");
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            // Native regions belong to the package, not the process. Keep a
            // cross-process lease before touching this feature's namespace.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockPath))!);
            lease ??= new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            monitor = GeofenceMonitor.Current;
            RemoveOwnedRegions(); monitor.ReadReports(); identities.Clear();
            var nonce = Guid.NewGuid().ToString("N");
            foreach (var place in places)
            {
                Add(place, NativeGeofenceTransition.Entered, place.EntryRadius, nonce);
                Add(place, NativeGeofenceTransition.Exited, place.ExitRadius, nonce);
            }
            token.ThrowIfCancellationRequested(); callback = report;
            monitor.GeofenceStateChanged += OnReports; monitor.StatusChanged += OnStatus;
            if (monitor.Status is GeofenceMonitorStatus.Disabled or GeofenceMonitorStatus.NotAvailable)
                throw new InvalidOperationException("Windows native geofencing is disabled/unavailable on this device.");
        });
    }
    private void Add(NativeGeofenceSpec place, NativeGeofenceTransition transition, double radius, string nonce)
    {
        var id = Prefix + nonce + "-" + Guid.NewGuid().ToString("N");
        var region = new Geocircle(new BasicGeoposition { Latitude = place.Latitude, Longitude = place.Longitude }, radius);
        var states = (transition == NativeGeofenceTransition.Entered ? MonitoredGeofenceStates.Entered : MonitoredGeofenceStates.Exited) | MonitoredGeofenceStates.Removed;
        var fence = new Geofence(id, region, states, false, TimeSpan.FromSeconds(10), DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        identities.Add(id, (place, transition)); monitor!.Geofences.Add(fence);
    }
    private void OnReports(GeofenceMonitor sender, object args)
    {
        // ReadReports is destructive. Capture callbacks/identity mapping with
        // the reports on the UI thread so a replacement session cannot inherit them.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var target = callback; if (target is null) return;
            try
            {
                var reports = sender.ReadReports().OrderBy(r => r.Geoposition.Coordinate.Timestamp).ToArray();
                var pending = new List<NativeGeofenceReport>();
                foreach (var item in reports)
                {
                    if (!identities.TryGetValue(item.Geofence.Id, out var identity)) continue;
                    var transition = item.NewState switch
                    {
                        GeofenceState.Entered => NativeGeofenceTransition.Entered,
                        GeofenceState.Exited => NativeGeofenceTransition.Exited,
                        _ => NativeGeofenceTransition.Removed
                    };
                    if (transition != NativeGeofenceTransition.Removed && transition != identity.Transition) continue;
                    var coordinate = item.Geoposition.Coordinate;
                    pending.Add(new(identity.Spec.ReminderId, identity.Spec.ArmId, transition,
                        new PositionFix(coordinate.Point.Position.Latitude, coordinate.Point.Position.Longitude, coordinate.Accuracy, coordinate.Timestamp)));
                }
                foreach (var item in pending) await target(item);
            }
            catch { await target(new(Guid.Empty, Guid.Empty, NativeGeofenceTransition.Unavailable, null)); }
        });
    }
    private void OnStatus(GeofenceMonitor sender, object args)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var target = callback;
            if (target is not null && sender.Status is GeofenceMonitorStatus.Disabled or GeofenceMonitorStatus.NotAvailable or GeofenceMonitorStatus.NoData)
                await target(new(Guid.Empty, Guid.Empty, NativeGeofenceTransition.Unavailable, null));
        });
    }
    public Task StopAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        callback = null;
        if (monitor is not null) { monitor.GeofenceStateChanged -= OnReports; monitor.StatusChanged -= OnStatus; }
        if (lease is null) return; // No native access/prompt for ordinary manual-only actions.
        RemoveOwnedRegions(); monitor?.ReadReports(); identities.Clear(); monitor = null;
        lease.Dispose(); lease = null;
    });
    private void RemoveOwnedRegions()
    {
        if (monitor is null) return;
        foreach (var fence in monitor.Geofences.Where(g => g.Id.StartsWith(Prefix, StringComparison.Ordinal)).ToArray()) monitor.Geofences.Remove(fence);
    }
}
