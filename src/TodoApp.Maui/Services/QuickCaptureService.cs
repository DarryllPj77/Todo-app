using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public sealed record CaptureDestination(string Label, string? Partition = null, int SessionVersion = 0,
    string? Origin = null, Guid? UserId = null)
{
    public static CaptureDestination Device { get; } = new("Device only");
}

// This boundary deliberately has no API client or sync engine: account capture
// only queues an operation in the existing origin/account cache.
public sealed class QuickCaptureService(SqliteLocalTaskService device, SqliteSyncStore sync,
    ClientSession session, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<CaptureDestination> SelectAccountAsync()
    {
        await session.RestoreAsync();
        var saved = await session.RequireAsync(allowUnverified: true);
        return new CaptureDestination($"Account: {saved.User.Email} at {saved.Origin} (queued offline)",
            SqliteSyncStore.Partition(saved.Origin, saved.User.Id), session.Version, saved.Origin, saved.User.Id);
    }

    public Task<QuickCaptureResult> SaveAsync(Guid id, QuickCaptureDraft draft, CaptureDestination destination)
    {
        if (destination.Partition is null) return device.CaptureAsync(id, draft);
        void Authorize()
        {
            session.EnsureVersion(destination.SessionVersion);
            var current = session.Current;
            if (current is null || current.Origin != destination.Origin || current.User.Id != destination.UserId ||
                current.Origin != session.Endpoint.AbsoluteUri || current.ExpiresAt <= clock.GetUtcNow() ||
                destination.Partition != SqliteSyncStore.Partition(current.Origin, current.User.Id))
                throw new ApiClientException("The account changed or expired. Close capture, sign in and start again.");
        }
        return sync.CaptureAsync(destination.Partition, id, draft, Authorize);
    }
}
