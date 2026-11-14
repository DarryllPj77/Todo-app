using TodoApp.Domain.Location;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

// Shared worker graph: no API client, account restoration, new location fix,
// foreground runtime or notification permission prompt is constructed here.
public static class DeviceBackgroundGeofenceHost
{
    public static BackgroundGeofenceService Create(string directory, ILocationEncryptionKeyStore keys,
        IBackgroundGeofencePlatform platform, IReminderPlatform notifications, IBackgroundGeofenceBlocker blocker, TimeProvider? clock = null)
    {
        var time = clock ?? TimeProvider.System;
        var journal = new SqliteBackgroundGeofenceStore(Path.Combine(directory, "geofence-background.db3"), keys);
        var guarded = new BackgroundNotificationGuard(notifications, async () =>
        {
            if (blocker.IsBlocked) return false;
            var run = await journal.ReadAsync();
            return !blocker.IsBlocked && run is { State: BackgroundGeofenceState.Active } && time.GetUtcNow() >= run.StartedUtc && time.GetUtcNow() < run.ExpiresUtc;
        });
        var session = new ClientSession(new NoAccountSettings(), new NoTokens(), time);
        var tasks = new LocationTaskCatalog(new SqliteLocalTaskService(Path.Combine(directory, "todoapp.db3")), new(Path.Combine(directory, "sync.db3")), session, time);
        var reminders = new LocationReminderService(new(Path.Combine(directory, "location.db3"), keys), tasks, new NoPositionRequests(), guarded, session, time);
        return new(journal, reminders, platform, blocker, new(Path.Combine(directory, "native-geofence.lock")), time);
    }
    private sealed class NoAccountSettings : IClientSettings
    { public string ApiBaseUrl { get; set; } = "https://background.invalid/"; public bool RestorationBlocked { get; set; } = true; }
    private sealed class NoTokens : ISecureTokenStore
    {
        public Task<string?> GetAsync() => throw new NotSupportedException("Account tokens are unavailable to a device worker.");
        public Task SetAsync(string value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }
    private sealed class NoPositionRequests : IForegroundLocationProvider
    { public Task<PositionFix> ReadAsync(CancellationToken token) => throw new NotSupportedException("A background worker cannot request location or permissions."); }
}
