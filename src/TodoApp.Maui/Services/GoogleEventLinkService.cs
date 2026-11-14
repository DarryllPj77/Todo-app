using System.Net;
using TodoApp.Domain.Calendar;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public sealed partial class GoogleEventLinkService(GoogleConnectionService connection, GoogleCalendarApi api, SqliteCalendarStore store, ClientSession session, TimeProvider? clock = null)
{
    public bool IsSupported => connection.IsSupported;
    public Task<IReadOnlyList<GoogleCalendarChoice>> CalendarsAsync(string scope, CancellationToken ct) =>
        connection.UseGrantAsync(scope, null, null, (grant, guard, token) => api.CalendarsAsync(grant, guard, token), ct);

    public Task<GoogleEventLink> PublishAsync(string scope, TimeBlock block, GoogleCalendarChoice calendar, long? linkRevision, CancellationToken ct) =>
        connection.UseGrantAsync(scope, null, null, async (grant, guard, token) =>
        {
            guard(); await api.RequireOwnerAsync(grant, calendar.Id, guard, token); guard();
            var pending = await store.StagePublishAsync(scope, block.Id, block.Revision, linkRevision, grant.Subject, grant.ClientId, calendar.Id, calendar.Name);
            guard(); return await ProcessAsync(grant, pending, guard, token);
        }, ct);

    public Task<GoogleEventLink> DeleteAsync(GoogleEventLink selected, CancellationToken ct) =>
        connection.UseGrantAsync(selected.Scope, selected.Subject, selected.ClientId, async (grant, guard, token) =>
        {
            guard(); await api.RequireOwnerAsync(grant, selected.CalendarId, guard, token); guard();
            var pending = await store.StageDeleteAsync(selected.Scope, selected.BlockId, selected.Revision);
            guard(); return await ProcessAsync(grant, pending, guard, token);
        }, ct);

    public Task<GoogleEventLink> RetryAsync(GoogleEventLink selected, CancellationToken ct) =>
        connection.UseGrantAsync(selected.Scope, selected.Subject, selected.ClientId, async (grant, guard, token) =>
        {
            guard(); var current = (await store.LinksAsync(selected.Scope)).SingleOrDefault(l => l.BlockId == selected.BlockId);
            guard(); if (current != selected) throw new CalendarConflictException();
            if (!selected.Pending) throw new ArgumentException("Only pending operations can be retried. Conflicts require separate review.");
            await api.RequireOwnerAsync(grant, selected.CalendarId, guard, token); guard();
            return await ProcessAsync(grant, selected, guard, token);
        }, ct);

    private async Task<GoogleEventLink> ProcessAsync(GoogleGrant grant, GoogleEventLink pending, Action guard, CancellationToken ct)
    {
        try
        {
            guard();
            // Recheck the durable operation before any provider write, including retries.
            var current = (await store.LinksAsync(pending.Scope)).SingleOrDefault(l => l.BlockId == pending.BlockId);
            guard(); if (current != pending) throw new CalendarConflictException();
            var remote = await api.GetAsync(grant, pending, guard, ct); guard();
            if (pending.State == GoogleLinkState.PendingDelete)
            {
                if (remote is not null)
                {
                    if (remote.ETag != pending.ETag) throw new GoogleEventConflictException("The Google event changed since it was linked. Deletion was blocked; review it in Google Calendar.");
                    await api.DeleteAsync(grant, pending, guard, ct); guard();
                }
                return await store.FinishLinkAsync(pending, GoogleLinkState.Deleted, pending.ETag);
            }
            if (remote is not null && remote.Matches(pending.Desired))
                return await store.FinishLinkAsync(pending, GoogleLinkState.Linked, remote.ETag);
            if (pending.State == GoogleLinkState.PendingCreate)
            {
                if (remote is not null) throw new GoogleEventConflictException("The reserved Google event already exists with different data. It was not overwritten.");
                try { remote = await api.WriteAsync(grant, pending, create: true, guard, ct); }
                catch (GoogleCalendarHttpException error) when (error.Status == HttpStatusCode.Conflict)
                {
                    remote = await api.GetAsync(grant, pending, guard, ct);
                    if (remote is null || !remote.Matches(pending.Desired))
                        throw new GoogleEventConflictException("The reserved event ID could not be reconciled safely. No replacement ID or duplicate event was created.");
                }
            }
            else
            {
                if (remote is null) throw new GoogleEventConflictException("The linked Google event was deleted. It was not recreated automatically.");
                if (remote.ETag != pending.ETag) throw new GoogleEventConflictException("The Google event changed independently. Local changes were not pushed over the unseen version.");
                remote = await api.WriteAsync(grant, pending, create: false, guard, ct);
            }
            guard();
            if (!remote.Matches(pending.Desired)) throw new GoogleEventConflictException("Google returned different event fields. The operation needs review.");
            return await store.FinishLinkAsync(pending, GoogleLinkState.Linked, remote.ETag);
        }
        catch (GoogleCalendarHttpException error) when (error.Status == HttpStatusCode.PreconditionFailed)
        {
            guard(); return await store.FinishLinkAsync(pending, GoogleLinkState.Conflict, pending.ETag, "Google rejected the event revision (412). Nothing was overwritten; review is required.");
        }
        catch (GoogleEventConflictException error)
        {
            guard(); return await store.FinishLinkAsync(pending, GoogleLinkState.Conflict, pending.ETag, error.Message);
        }
        // Network, cancellation and local persistence failures leave the same durable
        // operation/snapshot/ID intact. A later explicit retry reconciles before writing.
    }
}
