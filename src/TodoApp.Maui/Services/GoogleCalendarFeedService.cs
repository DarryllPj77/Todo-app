using System.Net;
using TodoApp.Domain.Calendar;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed class GoogleCalendarFeedService(GoogleConnectionService connection, GoogleCalendarApi api, SqliteCalendarStore store,
    ClientSession session, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    public bool IsSupported => connection.IsSupported;
    public Task<IReadOnlyList<GoogleCalendarChoice>> CalendarsAsync(string scope, CancellationToken ct) =>
        connection.UseGrantAsync(scope, null, null, (grant, guard, token) => api.CalendarsAsync(grant, guard, token), ct);
    public Task<GoogleCalendarFeed> EnableAsync(string scope, GoogleCalendarChoice calendar, CancellationToken ct) =>
        connection.UseGrantAsync(scope, null, null, async (grant, guard, token) =>
        {
            guard(); await api.RequireOwnerAsync(grant, calendar.Id, guard, token); guard();
            var profile = await store.FeedAsync(Binding(grant, calendar)); guard();
            return await store.SetFeedEnabledAsync(profile, true);
        }, ct);
    public async Task<GoogleCalendarFeed> DisableAsync(GoogleCalendarFeed feed, CancellationToken ct)
    {
        var version = session.Version; GuardScope(feed.Binding.Scope, version, ct);
        var disabled = await store.DisableFeedAsync(feed.Binding); GuardScope(feed.Binding.Scope, version, ct); return disabled;
    }
    public Task<GoogleFeedResult> ScanCalendarAsync(string scope, GoogleCalendarChoice calendar, CancellationToken ct) =>
        connection.UseGrantAsync(scope, null, null, async (grant, guard, token) =>
        {
            var profile = await store.FeedAsync(Binding(grant, calendar)); guard();
            var version = session.Version;
            try { return await RunScanAsync(grant, profile, manual: true, guard, token); }
            catch (Exception error) when (error is not CalendarConflictException && error is not OperationCanceledException)
            { await RecordFailureAsync(profile, error, version, ct); throw; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && session.Version == version)
            { await RecordFailureAsync(profile, new TimeoutException(), version, ct); throw; }
        }, ct);
    public async Task<GoogleFeedResult> ScanFeedAsync(GoogleCalendarFeed profile, bool manual, CancellationToken ct)
    {
        var version = session.Version; GuardScope(profile.Binding.Scope, version, ct);
        if (!manual && (!profile.Enabled || profile.RetryAfterUtc > time.GetUtcNow())) return new(profile, 0, 0, false, false, true);
        try
        {
            return await connection.UseGrantAsync(profile.Binding.Scope, profile.Binding.Subject, profile.Binding.ClientId,
                (grant, guard, token) => RunScanAsync(grant, profile, manual, guard, token), ct);
        }
        catch (Exception error) when (error is not CalendarConflictException && error is not OperationCanceledException)
        {
            await RecordFailureAsync(profile, error, version, ct); throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && session.Version == version)
        { await RecordFailureAsync(profile, new TimeoutException(), version, ct); throw; }
    }
    private async Task<GoogleFeedResult> RunScanAsync(GoogleGrant grant, GoogleCalendarFeed profile, bool manual, Action guard, CancellationToken ct)
    {
        guard();
        var current = await store.FeedAsync(profile.Binding); guard();
        if (current != profile) throw new CalendarConflictException();
        if (!manual && (!profile.Enabled || profile.RetryAfterUtc > time.GetUtcNow())) return new(profile, 0, 0, false, false, true);
        var cohort = (await store.LinksAsync(profile.Binding.Scope)).Where(l => profile.Binding.Matches(l)
            && l.State is not (GoogleLinkState.Deleted or GoogleLinkState.Detached)).ToArray(); guard();
        if (cohort.All(l => l.Pending))
        {
            var deferred = !manual && profile.Enabled ? await store.SnoozeEmptyFeedAsync(profile) : profile; guard();
            return new(deferred, 0, cohort.Length, false, false, true);
        }
        await api.RequireOwnerAsync(grant, profile.Binding.CalendarId, guard, ct); guard();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(TimeSpan.FromMinutes(2));
        var batch = await api.ChangesAsync(grant, profile, cohort, guard, time.GetUtcNow(), limit.Token); guard();
        return await store.CommitFeedAsync(profile, cohort, batch);
    }
    private async Task RecordFailureAsync(GoogleCalendarFeed profile, Exception error, int version, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || session.Version != version) return;
        try
        {
            GuardScope(profile.Binding.Scope, version, ct);
            var pause = error is GoogleAuthorizationException or GoogleEventConflictException
                || error is GoogleCalendarHttpException { Status: HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.BadRequest };
            await store.FailFeedAsync(profile, pause, (error as GoogleCalendarHttpException)?.RetryAfter);
        }
        catch (CalendarConflictException) { /* A newer window/setting owns the profile. */ }
        catch (GoogleAuthorizationException) { /* Old scope is no longer usable. */ }
    }
    private void GuardScope(string scope, int version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (session.Version != version || session.Current is not { } saved || !session.IsVerified || saved.ExpiresAt <= time.GetUtcNow()
            || SqliteSyncStore.Partition(saved.Origin, saved.User.Id) != scope) throw new GoogleAuthorizationException("Sign in to the original Todo account before checking its Google monitor.");
    }
    private static GoogleFeedBinding Binding(GoogleGrant grant, GoogleCalendarChoice calendar) => new(grant.Scope, grant.Subject, grant.ClientId, calendar.Id, calendar.Name);
}
