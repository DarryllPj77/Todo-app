using TodoApp.Domain.Location;
using TodoApp.Domain.Recurrence;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public interface ILocationTaskSource
{
    bool CanAccess(string scope);
    Task<IReadOnlyList<LocationTask>> ReadAsync(string scope);
}
public sealed class LocationTaskCatalog(ILocalTaskService device, SqliteSyncStore sync, ClientSession session, TimeProvider? clock = null) : ILocationTaskSource
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public bool CanAccess(string scope) => scope == "device" || session.Current is { } saved && saved.ExpiresAt > time.GetUtcNow()
        && SqliteSyncStore.Partition(saved.Origin, saved.User.Id) == scope;
    public async Task<IReadOnlyList<LocationTask>> ReadAsync(string scope)
    {
        if (!CanAccess(scope)) throw new ApiClientException("Sign in again to access this account's local places.");
        var version = session.Version;
        if (scope == "device") return (await device.GetTasksAsync()).Where(t => !t.IsCompleted)
            .Select(t => new LocationTask(t.Id, t.Title, t.Recurrence)).ToArray();
        var state = await sync.ReadAsync(scope); session.EnsureVersion(version);
        if (!CanAccess(scope)) throw new ApiClientException("The account session expired. Sign in again.");
        return state.Rows.Values.Where(r => !r.Conflict && r.Local is { IsCompleted: false })
            .Select(r => new LocationTask(r.Id, r.Local!.Title, r.Local.Recurrence ?? RepeatState.None)).ToArray();
    }
}
