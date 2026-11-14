using TodoApp.Domain.Location;

namespace TodoApp.Maui.Services;

public enum NativeGeofenceTransition { Entered, Exited, Removed, Unavailable }
public sealed record NativeGeofenceReport(Guid ReminderId, Guid ArmId, NativeGeofenceTransition Transition, PositionFix? Position);
public sealed record NativeGeofenceSpec(Guid ReminderId, Guid ArmId, double Latitude, double Longitude, int RadiusMeters)
{
    // Two conservative regions make an event near a native boundary usable by
    // the existing accuracy-aware rules. Maximum 10 places = 20 native fences.
    public double EntryRadius => RadiusMeters / 2d;
    public double ExitRadius => RadiusMeters + Math.Min(500, RadiusMeters / 2d) + 50;
    public override string ToString() => "Native geofence specification";
}
public interface INativeGeofencePlatform
{
    bool IsSupported { get; }
    string Description { get; }
    Task StartAsync(IReadOnlyList<NativeGeofenceSpec> places, Func<NativeGeofenceReport, Task> report, CancellationToken token);
    Task StopAsync();
}
public sealed class UnsupportedNativeGeofencePlatform : INativeGeofencePlatform
{
    public bool IsSupported => false;
    public string Description => "Native sessions are unavailable on this build. Packaged Windows is the first implementation; use manual arrival checks here. Closed-app monitoring is not enabled.";
    public Task StartAsync(IReadOnlyList<NativeGeofenceSpec> places, Func<NativeGeofenceReport, Task> report, CancellationToken token) => throw new InvalidOperationException(Description);
    public Task StopAsync() => Task.CompletedTask;
}
