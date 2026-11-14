using TodoApp.Domain.Calendar;
using TodoApp.Maui.Models;

namespace TodoApp.Maui.Services;

public enum GoogleReviewChoice { UseGoogle, KeepLocal, AcceptMissing }

public sealed partial class GoogleEventLinkService
{
    public Task<GoogleEventLink> ReviewAsync(GoogleEventLink selected, CancellationToken ct) =>
        connection.UseGrantAsync(selected.Scope, selected.Subject, selected.ClientId, async (grant, guard, token) =>
        {
            guard(); await CheckLinkAsync(selected, guard);
            if (selected.State is not (GoogleLinkState.Linked or GoogleLinkState.Conflict)) throw new ArgumentException("Retry pending work before reviewing; closed links are inactive.");
            await api.RequireOwnerAsync(grant, selected.CalendarId, guard, token); guard();
            var local = (await store.ReadAsync(selected.Scope)).SingleOrDefault(b => b.Id == selected.BlockId); guard();
            GoogleReviewKind kind; GoogleReviewedFields? fields = null; var problem = "";
            try
            {
                var remote = await api.GetAsync(grant, selected, guard, token); guard();
                if (remote is null) kind = GoogleReviewKind.Missing;
                else if (remote.Title.Length > 4096 || remote.StartUtc >= remote.EndUtc)
                { kind = GoogleReviewKind.Unsafe; problem = "Google returned fields outside the supported review format. Keep the event unchanged or detach locally."; }
                else { kind = GoogleReviewKind.Present; fields = Fields(remote); }
            }
            catch (GoogleEventConflictException error) { guard(); kind = GoogleReviewKind.Unsafe; problem = error.Message; }
            guard(); return await store.SaveReviewAsync(selected, local, kind, fields, problem);
        }, ct);

    public Task<GoogleEventLink> ResolveAsync(GoogleEventLink selected, GoogleReviewChoice choice, CancellationToken ct) =>
        connection.UseGrantAsync(selected.Scope, selected.Subject, selected.ClientId, async (grant, guard, token) =>
        {
            guard(); await CheckLinkAsync(selected, guard);
            var review = selected.Review ?? throw new ArgumentException("Load a Google/local review before choosing a resolution.");
            if (!Enum.IsDefined(choice) || choice == GoogleReviewChoice.AcceptMissing && review.Kind != GoogleReviewKind.Missing
                || choice != GoogleReviewChoice.AcceptMissing && review.Kind != GoogleReviewKind.Present)
                throw new ArgumentException("This resolution does not match the reviewed Google event.");
            await api.RequireOwnerAsync(grant, selected.CalendarId, guard, token); guard();
            var latest = await api.GetAsync(grant, selected, guard, token); guard();
            if (review.Kind == GoogleReviewKind.Missing ? latest is not null : latest is null || Fields(latest) != review.Remote)
                throw new GoogleEventConflictException("Google changed after the displayed review. Reload the review before deciding; nothing was overwritten.");
            guard();
            if (choice == GoogleReviewChoice.UseGoogle) return await store.AdoptReviewedGoogleAsync(selected);
            if (choice == GoogleReviewChoice.AcceptMissing) return await store.AcceptReviewedMissingAsync(selected);
            var pending = await store.StageReviewedLocalAsync(selected); guard();
            return await ProcessAsync(grant, pending, guard, token);
        }, ct);

    public async Task<GoogleEventLink> DetachAsync(GoogleEventLink selected, CancellationToken ct)
    {
        var version = session.Version;
        void Guard()
        {
            ct.ThrowIfCancellationRequested();
            if (session.Version != version || session.Current is not { } saved || !session.IsVerified
                || saved.ExpiresAt <= (clock ?? TimeProvider.System).GetUtcNow()
                || TodoApp.Infrastructure.Services.SqliteSyncStore.Partition(saved.Origin, saved.User.Id) != selected.Scope)
                throw new GoogleAuthorizationException("Sign in to the original Todo account before detaching its local link.");
        }
        Guard(); var detached = await store.DetachLinkAsync(selected); Guard(); return detached;
    }
    private async Task CheckLinkAsync(GoogleEventLink expected, Action guard)
    {
        var current = (await store.LinksAsync(expected.Scope)).SingleOrDefault(l => l.BlockId == expected.BlockId); guard();
        if (current != expected) throw new TodoApp.Infrastructure.Services.CalendarConflictException();
    }
    private static GoogleReviewedFields Fields(GoogleEventResource remote) => new(remote.ETag, remote.Title, remote.StartUtc, remote.EndUtc);
}
