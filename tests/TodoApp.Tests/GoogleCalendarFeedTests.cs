using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Calendar;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed partial class GoogleEventLinkTests
{
    private GoogleCalendarFeedService FeedService => new(connection, new GoogleCalendarApi(apiHttp), Store, client.Session, client.Clock);
    private GoogleFeedBinding Binding => new(Scope, "google-owner", GoogleClient, CalendarId, "Work");
    private static HttpResponseMessage Page(IEnumerable<JsonObject> items, string? cursor = "next-cursor", string? page = null) =>
        ClientHarness.Json(new { accessRole = "owner", items = items.ToArray(), nextSyncToken = cursor, nextPageToken = page });
    private IEnumerable<Request> CollectionRequests => provider.Requests.Where(r => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.EndsWith("/events"));
    [Fact]
    public async Task FeedSchemaThreeUpgradePreservesBlocksLinksAndDefaultsChecksOff()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block);
        using (var sql = new SqliteConnection($"Data Source={Path.Combine(directory, "calendar.db3")};Pooling=False"))
        {
            await sql.OpenAsync(); using var command = sql.CreateCommand(); command.CommandText = "DROP TABLE GoogleCalendarFeeds; PRAGMA user_version=3;"; await command.ExecuteNonQueryAsync();
        }
        var result = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        Assert.False(result.Feed.Enabled); Assert.Equal("cursor-default", result.Feed.Cursor); Assert.Equal(0, result.Notices);
        Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope))); Assert.Equal(linked, Assert.Single(await Store.LinksAsync(Scope)));
        using var check = new SqliteConnection($"Data Source={Path.Combine(directory, "calendar.db3")};Pooling=False"); await check.OpenAsync();
        using var version = check.CreateCommand(); version.CommandText = "PRAGMA user_version;"; Assert.Equal(4L, await version.ExecuteScalarAsync());
    }
    [Fact]
    public async Task CompletePagedFeedCommitsOnlyLinkedNoticesAndDiscardsUnrelatedPayloads()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); ChangeRemote(link);
        provider.CollectionResponse = (request, _) => Task.FromResult(GoogleOAuthProtocol.Query(request.RequestUri!).ContainsKey("pageToken")
            ? Page([provider.Events[link.EventId]], "opaque +& cursor")
            : Page([new JsonObject { ["id"] = "unlinked-event", ["summary"] = "UNRELATED_PRIVATE_TEXT" }], null, "page +&"));
        var result = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        Assert.True(result.Full); Assert.Equal(1, result.Notices); Assert.Equal("opaque +& cursor", result.Feed.Cursor);
        var detected = Assert.Single(await Store.LinksAsync(Scope)); Assert.Equal(GoogleNoticeKind.Changed, detected.Notice?.Kind);
        Assert.Equal(link.State, detected.State); Assert.Equal(link.Published, detected.Published); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
        Assert.DoesNotContain("UNRELATED_PRIVATE_TEXT", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "calendar.db3"))));
        var second = GoogleOAuthProtocol.Query(CollectionRequests.Last().Uri); Assert.Equal("page +&", second["pageToken"]);
        Assert.Equal(0, provider.Patches); Assert.Equal(0, provider.Deletes);
    }
    [Fact]
    public async Task IncrementalScanReusesCursorAndStableQueryWithoutInferringMissingEvents()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var first = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        provider.CollectionResponse = (_, _) => Task.FromResult(Page([], "incremental-next"));
        var result = await FeedService.ScanFeedAsync(first.Feed, true, CancellationToken.None);
        var query = GoogleOAuthProtocol.Query(CollectionRequests.Last().Uri);
        Assert.Equal("cursor-default", query["syncToken"]); Assert.Equal("false", query["singleEvents"]); Assert.Equal("true", query["showDeleted"]); Assert.Equal("100", query["maxResults"]);
        Assert.DoesNotContain("timeMin", query.Keys); Assert.DoesNotContain("privateExtendedProperty", query.Keys);
        Assert.False(result.Full); Assert.Equal(0, result.Notices); Assert.Equal(link, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task FailedLaterPageRetainsCursorAndAppliesNoPartialNotices()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var first = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None); ChangeRemote(link);
        provider.CollectionResponse = (request, _) => Task.FromResult(GoogleOAuthProtocol.Query(request.RequestUri!).ContainsKey("pageToken")
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Page([provider.Events[link.EventId]], null, "later"));
        await Assert.ThrowsAsync<GoogleCalendarHttpException>(() => FeedService.ScanFeedAsync(first.Feed, true, CancellationToken.None));
        var saved = Assert.Single(await Store.FeedsAsync(Scope)); Assert.Equal(first.Feed.Cursor, saved.Cursor); Assert.Equal(1, saved.Failures);
        Assert.Equal(link, Assert.Single(await Store.LinksAsync(Scope))); Assert.True(saved.RetryAfterUtc > client.Clock.Now);
    }
    [Theory]
    [InlineData("repeat-page")]
    [InlineData("missing-final")]
    [InlineData("both-tokens")]
    [InlineData("duplicate-event")]
    public async Task InvalidPageSequencesNeverAdvanceCursor(string invalid)
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var baseline = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        var calls = 0;
        provider.CollectionResponse = (_, _) =>
        {
            calls++;
            return Task.FromResult(invalid switch
            {
                "repeat-page" => Page([], null, "same"),
                "missing-final" => Page([], null),
                "both-tokens" => Page([], "next", "page"),
                _ => calls == 1 ? Page([provider.Events[link.EventId]], null, "page") : Page([provider.Events[link.EventId]], "next")
            });
        };
        await Assert.ThrowsAsync<GoogleEventConflictException>(() => FeedService.ScanFeedAsync(baseline.Feed, true, CancellationToken.None));
        Assert.Equal(baseline.Feed.Cursor, (await Store.FeedAsync(Binding)).Cursor); Assert.Equal(link, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task ExpiredCursorPerformsFullResetWithoutTouchingPendingIntentOrBlocks()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var first = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        var other = await Store.SaveAsync(Scope, Guid.NewGuid(), new(Guid.NewGuid(), "Pending", block.WallStart.AddHours(2), "UTC", 30), null);
        var pending = await Store.StagePublishAsync(Scope, other.Id, other.Revision, null, Binding.Subject, Binding.ClientId, CalendarId, "Work");
        ChangeRemote(link);
        provider.CollectionResponse = (request, _) => Task.FromResult(GoogleOAuthProtocol.Query(request.RequestUri!).ContainsKey("syncToken")
            ? new HttpResponseMessage(HttpStatusCode.Gone) : Page(provider.Events.Values, "reset-cursor"));
        var result = await FeedService.ScanFeedAsync(first.Feed, true, CancellationToken.None);
        Assert.True(result.Reset); Assert.True(result.Full); Assert.Equal("reset-cursor", result.Feed.Cursor); Assert.Equal(1, result.PendingSkipped);
        Assert.Equal(pending, (await Store.LinksAsync(Scope)).Single(l => l.BlockId == pending.BlockId)); Assert.Equal(2, (await Store.ReadAsync(Scope)).Count);
    }
    [Fact]
    public async Task ResetDiscardsPre410PartialBatchAndFailedResetRetainsOldCursor()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var first = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        var changed = provider.Events[link.EventId].DeepClone().AsObject(); changed["summary"] = "Partial only"; changed["etag"] = "\"partial\"";
        provider.CollectionResponse = (request, _) =>
        {
            var query = GoogleOAuthProtocol.Query(request.RequestUri!);
            return Task.FromResult(query.ContainsKey("syncToken") ? query.ContainsKey("pageToken") ? new HttpResponseMessage(HttpStatusCode.Gone) : Page([changed], null, "p")
                : Page(provider.Events.Values, "reset-ok"));
        };
        var reset = await FeedService.ScanFeedAsync(first.Feed, true, CancellationToken.None); Assert.Equal(0, reset.Notices); Assert.Equal(link, Assert.Single(await Store.LinksAsync(Scope)));
        provider.CollectionResponse = (request, _) => Task.FromResult(new HttpResponseMessage(GoogleOAuthProtocol.Query(request.RequestUri!).ContainsKey("syncToken") ? HttpStatusCode.Gone : HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<GoogleCalendarHttpException>(() => FeedService.ScanFeedAsync(reset.Feed, true, CancellationToken.None));
        Assert.Equal("reset-ok", (await Store.FeedAsync(Binding)).Cursor); Assert.Equal(link, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task FullAbsenceAndCancelledDeltaCreateNoticesWithoutDeletingLocalData()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block);
        provider.CollectionResponse = (_, _) => Task.FromResult(Page([], "full-missing"));
        var full = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        var noticed = Assert.Single(await Store.LinksAsync(Scope)); Assert.Equal(GoogleNoticeKind.Missing, noticed.Notice?.Kind);
        provider.CollectionResponse = (_, _) => Task.FromResult(Page([new JsonObject { ["id"] = link.EventId, ["status"] = "cancelled" }], "delta-missing"));
        var delta = await FeedService.ScanFeedAsync(full.Feed, true, CancellationToken.None);
        Assert.Equal(0, delta.Notices); Assert.Equal(noticed, Assert.Single(await Store.LinksAsync(Scope))); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
    }
    [Fact]
    public async Task FeedInvalidatesStaleReviewButPreservesReviewOfTheSameProviderVersion()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var review = await Service.ReviewAsync(link, CancellationToken.None); ChangeRemote(link);
        var scan = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        var noticed = Assert.Single(await Store.LinksAsync(Scope)); Assert.Null(noticed.Review); Assert.NotNull(noticed.Notice);
        await Assert.ThrowsAsync<CalendarConflictException>(() => Service.ResolveAsync(review, GoogleReviewChoice.UseGoogle, CancellationToken.None));
        var fresh = await Service.ReviewAsync(noticed, CancellationToken.None); Assert.Null(fresh.Notice);
        await FeedService.ScanFeedAsync(scan.Feed, true, CancellationToken.None); Assert.Equal(fresh, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task UnsafeChangeOnlyFlagsReviewAndPendingRowsRemainByteEquivalent()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); provider.Events[link.EventId]["attendees"] = new JsonArray(new JsonObject { ["email"] = "guest@example.invalid" });
        var other = await Store.SaveAsync(Scope, Guid.NewGuid(), new(Guid.NewGuid(), "Pending", block.WallStart.AddHours(1), "UTC", 30), null);
        var pending = await Store.StagePublishAsync(Scope, other.Id, other.Revision, null, Binding.Subject, Binding.ClientId, CalendarId, "Work");
        var result = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        Assert.Equal(1, result.PendingSkipped); Assert.Equal(GoogleNoticeKind.Unsafe, (await Store.LinksAsync(Scope)).Single(l => l.BlockId == link.BlockId).Notice?.Kind);
        Assert.Equal(pending, (await Store.LinksAsync(Scope)).Single(l => l.BlockId == pending.BlockId)); Assert.Equal(0, provider.Patches); Assert.Equal(0, provider.Deletes);
    }
    [Theory]
    [InlineData("link")]
    [InlineData("membership")]
    [InlineData("disabled")]
    public async Task ConcurrentCohortOrProfileChangeRejectsCommit(string change)
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var enabled = await FeedService.EnableAsync(Scope, Calendar, CancellationToken.None);
        provider.CollectionResponse = async (_, _) =>
        {
            if (change == "link") await Store.StageDeleteAsync(Scope, link.BlockId, link.Revision);
            if (change == "membership")
            {
                var other = await Store.SaveAsync(Scope, Guid.NewGuid(), new(Guid.NewGuid(), "New link", block.WallStart.AddHours(1), "UTC", 30), null);
                await Store.StagePublishAsync(Scope, other.Id, other.Revision, null, Binding.Subject, Binding.ClientId, CalendarId, "Work");
            }
            if (change == "disabled") await Store.SetFeedEnabledAsync(enabled, false);
            return Page(provider.Events.Values, "must-not-commit");
        };
        await Assert.ThrowsAsync<CalendarConflictException>(() => FeedService.ScanFeedAsync(enabled, true, CancellationToken.None));
        Assert.Null((await Store.FeedAsync(Binding)).Cursor);
        if (change == "disabled") Assert.False((await Store.FeedAsync(Binding)).Enabled);
    }
    [Fact]
    public async Task SavedOptInBackoffRetryAfterAndManualOverrideAreHonored()
    {
        var block = await SetupAsync(); await PublishAsync(block); var off = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        var calls = provider.Requests.Count; Assert.True((await FeedService.ScanFeedAsync(off.Feed, false, CancellationToken.None)).Skipped); Assert.Equal(calls, provider.Requests.Count);
        var enabled = await FeedService.EnableAsync(Scope, Calendar, CancellationToken.None);
        provider.CollectionResponse = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromMinutes(10)); return Task.FromResult(response);
        };
        await Assert.ThrowsAsync<GoogleCalendarHttpException>(() => FeedService.ScanFeedAsync(enabled, false, CancellationToken.None));
        var failed = await Store.FeedAsync(Binding); Assert.True(failed.Enabled); Assert.True(failed.RetryAfterUtc >= client.Clock.Now.AddMinutes(10));
        calls = provider.Requests.Count; Assert.True((await FeedService.ScanFeedAsync(failed, false, CancellationToken.None)).Skipped); Assert.Equal(calls, provider.Requests.Count);
        provider.CollectionResponse = null; var manual = await FeedService.ScanFeedAsync(failed, true, CancellationToken.None);
        Assert.Null(manual.Feed.RetryAfterUtc); Assert.Equal(0, manual.Feed.Failures); Assert.True(manual.Feed.Enabled);
    }
    [Fact]
    public async Task AuthFailurePausesAndLocalDisableWorksWithoutGoogleCredentials()
    {
        var block = await SetupAsync(); await PublishAsync(block); var enabled = await FeedService.EnableAsync(Scope, Calendar, CancellationToken.None); vault.Values.Clear();
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => FeedService.ScanFeedAsync(enabled, false, CancellationToken.None));
        var paused = await Store.FeedAsync(Binding); Assert.False(paused.Enabled); Assert.Contains("paused", paused.Problem);
        var reenabled = await Store.SetFeedEnabledAsync(paused, true); var calls = provider.Requests.Count; var oauth = oauthHandler.Requests.Count;
        var disabled = await FeedService.DisableAsync(reenabled, CancellationToken.None);
        Assert.False(disabled.Enabled); Assert.Equal(calls, provider.Requests.Count); Assert.Equal(oauth, oauthHandler.Requests.Count);
    }
    [Fact]
    public async Task CancellationOrAccountSwitchDuringPagesCannotCommitOldScopeResults()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var scope = Scope; var binding = Binding;
        provider.CollectionResponse = async (_, _) => { await client.Session.SignOutAsync(); return Page(provider.Events.Values, "old-scope-cursor"); };
        await Assert.ThrowsAnyAsync<Exception>(() => FeedService.ScanCalendarAsync(scope, Calendar, CancellationToken.None));
        Assert.Empty(await Store.FeedsAsync(scope)); Assert.Equal(link, Assert.Single(await Store.LinksAsync(scope)));
        await client.LoginAsync(); using var cancellation = new CancellationTokenSource();
        provider.CollectionResponse = (_, _) => { cancellation.Cancel(); return Task.FromResult(Page(provider.Events.Values, "canceled-cursor")); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FeedService.ScanCalendarAsync(scope, Calendar, cancellation.Token));
        Assert.Empty(await Store.FeedsAsync(scope));
    }
    [Fact]
    public async Task MonitorConfirmationCancelAndScopeIsolationCannotEnableAnotherAccount()
    {
        var block = await SetupAsync(); await PublishAsync(block); var navigation = new ClientNavigation();
        using var vm = new GoogleFeedViewModel(client.Session, Store, FeedService, navigation, client.Clock);
        await vm.LoadCalendarsCommand.ExecuteAsync(); vm.SelectedCalendar = Assert.Single(vm.Calendars); await vm.EnableCommand.ExecuteAsync(); Assert.Empty(await Store.FeedsAsync(Scope));
        var scope = Scope; navigation.GoogleEventConfirmation = async () => { await client.Session.SignOutAsync(); return true; };
        await vm.EnableCommand.ExecuteAsync(); Assert.Empty(vm.Feeds); Assert.Empty(vm.Calendars); Assert.Empty(await Store.FeedsAsync(scope));
    }
    [Fact]
    public async Task CorruptFeedBindingFailsWithoutResettingLinksOrBlocks()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None);
        using (var sql = new SqliteConnection($"Data Source={Path.Combine(directory, "calendar.db3")};Pooling=False"))
        {
            await sql.OpenAsync(); using var command = sql.CreateCommand(); command.CommandText = "UPDATE GoogleCalendarFeeds SET Subject='wrong-owner';"; await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.FeedsAsync(Scope)); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope))); Assert.Equal(link, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task FullScanBoundPreventsPartialCommitAndPendingOnlyCalendarIsSkipped()
    {
        var block = await SetupAsync(); var pending = await Store.StagePublishAsync(Scope, block.Id, block.Revision, null, Binding.Subject, Binding.ClientId, CalendarId, "Work");
        var skipped = await FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None); Assert.True(skipped.Skipped); Assert.Empty(CollectionRequests);
        await Service.RetryAsync(pending, CancellationToken.None);
        provider.CollectionResponse = (_, _) => Task.FromResult(Page(Enumerable.Range(0, 10001).Select(i => new JsonObject { ["id"] = "unlinked" + i }), "too-large"));
        await Assert.ThrowsAsync<GoogleEventConflictException>(() => FeedService.ScanCalendarAsync(Scope, Calendar, CancellationToken.None)); Assert.Null((await Store.FeedAsync(Binding)).Cursor);
    }
    [Fact]
    public async Task StaleSelectionCanDisableAfterAnotherWindowAdvancedCursor()
    {
        var block = await SetupAsync(); await PublishAsync(block); var selected = await FeedService.EnableAsync(Scope, Calendar, CancellationToken.None);
        var scanned = await FeedService.ScanFeedAsync(selected, true, CancellationToken.None);
        Assert.True(scanned.Feed.Revision > selected.Revision);
        var disabled = await FeedService.DisableAsync(selected, CancellationToken.None);
        Assert.False(disabled.Enabled); Assert.Equal(scanned.Feed.Cursor, disabled.Cursor); Assert.True(disabled.Revision > scanned.Feed.Revision);
    }
    [Fact]
    public async Task AutomaticPendingOnlyCheckDefersWithoutAdvancingCursorOrCountingFailure()
    {
        var block = await SetupAsync(); var pending = await Store.StagePublishAsync(Scope, block.Id, block.Revision, null, Binding.Subject, Binding.ClientId, CalendarId, "Work");
        var enabled = await FeedService.EnableAsync(Scope, Calendar, CancellationToken.None);
        var result = await FeedService.ScanFeedAsync(enabled, false, CancellationToken.None);
        Assert.True(result.Skipped); Assert.True(result.Feed.Enabled); Assert.Equal(0, result.Feed.Failures); Assert.Null(result.Feed.Cursor);
        Assert.True(result.Feed.RetryAfterUtc > client.Clock.Now); Assert.Empty(CollectionRequests); Assert.Equal(pending, Assert.Single(await Store.LinksAsync(Scope)));
    }
}
