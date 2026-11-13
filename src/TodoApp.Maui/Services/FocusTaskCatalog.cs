using TodoApp.Domain.Focus;
using TodoApp.Domain.Recurrence;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public interface IFocusTaskSource
{
    bool CanAccess(string scope);
    Task<IReadOnlyList<FocusTask>> ReadAsync(string scope);
}
public sealed class FocusTaskCatalog(ILocalTaskService device, SqliteSyncStore sync, ClientSession session, TimeProvider? clock = null) : IFocusTaskSource
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public bool CanAccess(string scope) => scope == "device" || session.Current is { } saved && saved.ExpiresAt > time.GetUtcNow() &&
        SqliteSyncStore.Partition(saved.Origin, saved.User.Id) == scope;
    public async Task<IReadOnlyList<FocusTask>> ReadAsync(string scope)
    {
        if (!CanAccess(scope)) throw new ApiClientException("Sign in again to read this account's focus history and tasks.");
        var version = session.Version;
        if (scope == "device") return (await device.GetTasksAsync()).Where(t => !t.IsCompleted)
            .Select(t => new FocusTask(scope, t.Id, t.Title, t.Recurrence)).ToArray();
        var state = await sync.ReadAsync(scope); session.EnsureVersion(version);
        if (!CanAccess(scope)) throw new ApiClientException("The account session expired. Sign in again.");
        return state.Rows.Values.Where(r => r.Local is { IsCompleted: false })
            .Select(r => new FocusTask(scope, r.Id, r.Local!.Title, r.Local.Recurrence ?? RepeatState.None)).ToArray();
    }
}
