using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Calendar;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class GoogleEventLinkTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_links_" + Guid.NewGuid().ToString("N"));
    private readonly ClientHarness client = new();
    private readonly Vault vault = new();
    private readonly Provider provider = new();
    private readonly ClientHandler oauthHandler = new();
    private readonly HttpClient oauthHttp, apiHttp;
    private readonly GoogleConnectionService connection;
    private SqliteCalendarStore Store => new(Path.Combine(directory, "calendar.db3"), client.Clock);
    private GoogleEventLinkService Service => new(connection, new GoogleCalendarApi(apiHttp), Store);
    private const string GoogleClient = "12345-client.apps.googleusercontent.com";
    private const string CalendarId = "owned@example.invalid";
    private GoogleCalendarChoice Calendar => new(CalendarId, "Work");
    private string Scope => SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Session.Current.User.Id);
    public GoogleEventLinkTests()
    {
        oauthHandler.Response = (_, _) => Task.FromResult(ClientHarness.Json(new { sub = "google-owner", email = "google@example.invalid", email_verified = true }));
        oauthHttp = new(oauthHandler); apiHttp = new(provider);
        connection = new(client.Session, vault, new Broker(), new(oauthHttp, client.Clock), client.Clock);
    }
    private async Task<TimeBlock> SetupAsync()
    {
        await client.LoginAsync();
        vault.Values[Scope] = JsonSerializer.Serialize(new GoogleGrant { Scope = Scope, ClientId = GoogleClient, Subject = "google-owner", Email = "google@example.invalid",
            AccessToken = "google-access", RefreshToken = "google-refresh", GrantedScopes = GoogleOAuthProtocol.Scopes, ExpiresAt = client.Clock.Now.AddHours(1) });
        return await Store.SaveAsync(Scope, Guid.NewGuid(), new(Guid.NewGuid(), "Plan review", new DateTime(2026, 11, 15, 9, 0, 0), "UTC", 30), null);
    }
    private Task<GoogleEventLink> PublishAsync(TimeBlock block, long? revision = null) => Service.PublishAsync(Scope, block, Calendar, revision, CancellationToken.None);
    private Task<TimeBlock> RescheduleAsync(TimeBlock block, int minutes = 45) => Store.SaveAsync(Scope, block.Id,
        new(block.TaskId, block.Title, block.WallStart, block.TimeZoneId, minutes), block.Revision);
    public void Dispose() { connection.Dispose(); apiHttp.Dispose(); oauthHttp.Dispose(); client.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task SchemaOneUpgradePreservesBlocksAndRejectsCorruptLinkIdentity()
    {
        var block = await SetupAsync();
        using (var sql = new SqliteConnection($"Data Source={Path.Combine(directory, "calendar.db3")};Pooling=False"))
        {
            await sql.OpenAsync(); using var command = sql.CreateCommand(); command.CommandText = "DROP TABLE GoogleEventLinks; PRAGMA user_version=1;"; await command.ExecuteNonQueryAsync();
        }
        Assert.Empty(await Store.LinksAsync(Scope)); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
        var link = await PublishAsync(block);
        using (var sql = new SqliteConnection($"Data Source={Path.Combine(directory, "calendar.db3")};Pooling=False"))
        {
            await sql.OpenAsync(); using var command = sql.CreateCommand(); command.CommandText = "UPDATE GoogleEventLinks SET Revision=99;"; await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.LinksAsync(Scope)); Assert.Single(await Store.ReadAsync(Scope));
    }
    [Fact]
    public async Task PublishUsesReservedIdPrivateMarkersAndNoTodoTokenOrTaskDescription()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block);
        Assert.Equal(GoogleLinkState.Linked, link.State); Assert.Equal(block.Revision, link.Published?.BlockRevision);
        Assert.Equal("todo" + link.LinkId.ToString("N"), link.EventId); Assert.Equal(1, provider.Creates);
        var item = provider.Events[link.EventId]; Assert.Equal(block.Title, item["summary"]!.GetValue<string>());
        Assert.Equal(link.Proof, item["extendedProperties"]!["private"]!["todoProof"]!.GetValue<string>());
        Assert.False(item["reminders"]!["useDefault"]!.GetValue<bool>()); Assert.Null(item["description"]); Assert.Null(item["attendees"]);
        Assert.All(provider.Requests, request => { Assert.Equal("google-access", request.Token); Assert.Equal("www.googleapis.com", request.Uri.Host); });
    }
    [Fact]
    public async Task LostCreateResponseReopensAndReconcilesSameEventWithoutDuplicate()
    {
        var block = await SetupAsync(); provider.LoseCreate = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => PublishAsync(block));
        var pending = Assert.Single(await Store.LinksAsync(Scope)); Assert.Equal(GoogleLinkState.PendingCreate, pending.State);
        provider.HideNextGet = true; // Event visibility lag forces same-ID POST/409 reconciliation.
        var recovered = await Service.RetryAsync(pending, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Linked, recovered.State); Assert.Equal(pending.EventId, recovered.EventId);
        Assert.Single(provider.Events); Assert.Equal(1, provider.Creates);
    }
    [Fact]
    public async Task RetryUsesCapturedSnapshotEvenIfLocalBlockWasEdited()
    {
        var block = await SetupAsync(); provider.LoseCreate = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => PublishAsync(block)); var pending = Assert.Single(await Store.LinksAsync(Scope));
        var changed = await RescheduleAsync(block, 90); var recovered = await Service.RetryAsync(pending, CancellationToken.None);
        Assert.Equal(block.Revision, recovered.Published?.BlockRevision); Assert.Equal(block.EndUtc, recovered.Published?.EndUtc);
        var pushed = await PublishAsync(changed, recovered.Revision); Assert.Equal(changed.EndUtc, pushed.Published?.EndUtc); Assert.Equal(1, provider.Patches);
    }
    [Fact]
    public async Task ConditionalPatchAndLostReplyRecoverWithoutRepeatingMutation()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); var changed = await RescheduleAsync(block);
        provider.LosePatch = true; await Assert.ThrowsAsync<HttpRequestException>(() => PublishAsync(changed, linked.Revision));
        var pending = Assert.Single(await Store.LinksAsync(Scope)); Assert.Equal(GoogleLinkState.PendingUpdate, pending.State);
        var patched = Assert.Single(provider.Requests, request => request.Method == HttpMethod.Patch);
        Assert.Equal(linked.ETag, patched.ETag); Assert.DoesNotContain("attendees", patched.Body);
        Assert.Equal(GoogleLinkState.Linked, (await Service.RetryAsync(pending, CancellationToken.None)).State); Assert.Equal(1, provider.Patches);
    }
    [Fact]
    public async Task ExternalEditFreezesConflictWithoutOverwritingRemoteOrLocal()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var changed = await RescheduleAsync(block);
        provider.Events[link.EventId]["summary"] = "External review"; provider.Events[link.EventId]["etag"] = "\"external\"";
        var conflict = await PublishAsync(changed, link.Revision);
        Assert.Equal(GoogleLinkState.Conflict, conflict.State); Assert.Equal(0, provider.Patches);
        Assert.Equal("External review", provider.Events[link.EventId]["summary"]!.GetValue<string>());
        Assert.Equal(changed, Assert.Single(await Store.ReadAsync(Scope)));
        await Assert.ThrowsAsync<ArgumentException>(() => Service.RetryAsync(conflict, CancellationToken.None));
    }
    [Theory]
    [InlineData("proof")]
    [InlineData("attendees")]
    [InlineData("recurrence")]
    public async Task ForeignOrExtendedEventCannotBeOverwrittenOrDeleted(string edit)
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var remote = provider.Events[link.EventId];
        if (edit == "proof") remote["extendedProperties"]!["private"]!["todoProof"] = "foreign";
        if (edit == "attendees") remote["attendees"] = new JsonArray(new JsonObject { ["email"] = "guest@example.invalid" });
        if (edit == "recurrence") remote["recurrence"] = new JsonArray("RRULE:FREQ=DAILY");
        var result = await Service.DeleteAsync(link, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Conflict, result.State); Assert.Equal(0, provider.Deletes); Assert.Single(provider.Events);
    }
    [Fact]
    public async Task Provider412StopsConditionalWriteAndPersistsConflict()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); var changed = await RescheduleAsync(block);
        provider.PreconditionFailure = true;
        var result = await PublishAsync(changed, link.Revision);
        Assert.Equal(GoogleLinkState.Conflict, result.State); Assert.Contains("412", result.Problem); Assert.Equal(0, provider.Patches);
    }
    [Fact]
    public async Task LostDeleteResponseReconcilesAbsenceAndRetainsLocalBlock()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); provider.LoseDelete = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Service.DeleteAsync(linked, CancellationToken.None));
        var pending = Assert.Single(await Store.LinksAsync(Scope)); Assert.Equal(GoogleLinkState.PendingDelete, pending.State);
        Assert.Equal(GoogleLinkState.Deleted, (await Service.RetryAsync(pending, CancellationToken.None)).State);
        Assert.Equal(1, provider.Deletes); Assert.Empty(provider.Events); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
    }
    [Fact]
    public async Task RepublishAfterDeletionGetsNewIdentityRatherThanReusingTombstone()
    {
        var block = await SetupAsync(); var first = await PublishAsync(block);
        var removed = await Service.DeleteAsync(first, CancellationToken.None); var second = await PublishAsync(block, removed.Revision);
        Assert.NotEqual(first.EventId, second.EventId); Assert.NotEqual(first.Proof, second.Proof); Assert.True(second.Revision > removed.Revision);
        Assert.Single(provider.Events);
    }
    [Fact]
    public async Task LocalDeletionKeepsLinkAvailableForExplicitRemoteCleanup()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); await Store.DeleteAsync(Scope, block.Id, block.Revision);
        Assert.Single(await Store.LinksAsync(Scope)); Assert.Single(provider.Events);
        Assert.Equal(GoogleLinkState.Deleted, (await Service.DeleteAsync(link, CancellationToken.None)).State);
    }
    [Fact]
    public async Task RemoteDeletionIsNotAutomaticallyRecreatedByUpdate()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); provider.Events.Remove(link.EventId);
        var changed = await RescheduleAsync(block); var result = await PublishAsync(changed, link.Revision);
        Assert.Equal(GoogleLinkState.Conflict, result.State); Assert.Equal(1, provider.Creates); Assert.Empty(provider.Events);
    }
    [Fact]
    public async Task StaleBlockOrLinkRevisionCannotStageAnotherMutation()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); await RescheduleAsync(block);
        await Assert.ThrowsAsync<CalendarConflictException>(() => PublishAsync(block, linked.Revision));
        await Assert.ThrowsAsync<CalendarConflictException>(() => PublishAsync(block, linked.Revision - 1));
        Assert.Equal(GoogleLinkState.Linked, Assert.Single(await Store.LinksAsync(Scope)).State); Assert.Equal(0, provider.Patches);
    }
    [Fact]
    public async Task DestinationCalendarAndGoogleIdentityArePinnedAfterLinking()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); var changed = await RescheduleAsync(block);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.PublishAsync(Scope, changed, new(CalendarId + ".other", "Other"), linked.Revision, CancellationToken.None));
        var grant = JsonSerializer.Deserialize<GoogleGrant>(vault.Values[Scope])!;
        vault.Values[Scope] = JsonSerializer.Serialize(new GoogleGrant { Scope = Scope, Subject = "other-subject", ClientId = grant.ClientId, Email = grant.Email,
            AccessToken = grant.AccessToken, RefreshToken = grant.RefreshToken, GrantedScopes = grant.GrantedScopes, ExpiresAt = grant.ExpiresAt });
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => Service.DeleteAsync(linked, CancellationToken.None)); Assert.Single(provider.Events);
    }
    [Fact]
    public async Task OwnerCheckPreventsStagingAndListExcludesUnownedCalendars()
    {
        var block = await SetupAsync(); var choices = await Service.CalendarsAsync(Scope, CancellationToken.None);
        Assert.Equal(CalendarId, Assert.Single(choices).Id);
        provider.Owner = false; await Assert.ThrowsAsync<GoogleEventConflictException>(() => PublishAsync(block));
        Assert.Empty(await Store.LinksAsync(Scope)); Assert.Empty(provider.Events);
    }
    [Fact]
    public async Task DeviceAndOtherTodoScopeCannotUseCurrentGoogleGrant()
    {
        var block = await SetupAsync();
        await Assert.ThrowsAsync<GoogleAuthorizationException>(() => Service.PublishAsync("device", block, Calendar, null, CancellationToken.None));
        Assert.Empty(provider.Requests); Assert.Empty(await Store.LinksAsync("device"));
    }
    [Fact]
    public async Task ConfirmationCancelAndLogoutCannotPublishOrDeleteOldScope()
    {
        var block = await SetupAsync(); var navigation = new ClientNavigation();
        using var vm = new GoogleEventsViewModel(client.Session, Store, Service, navigation, client.Clock);
        await vm.RefreshCommand.ExecuteAsync(); await vm.LoadCalendarsCommand.ExecuteAsync();
        vm.SelectedBlock = Assert.Single(vm.Blocks); vm.SelectedCalendar = Assert.Single(vm.Calendars);
        await vm.PublishCommand.ExecuteAsync(); Assert.Empty(provider.Events); Assert.Empty(await Store.LinksAsync(Scope));
        var originalScope = Scope;
        navigation.GoogleEventConfirmation = async () => { await client.Session.SignOutAsync(); return true; };
        await vm.PublishCommand.ExecuteAsync(); Assert.Empty(provider.Events); Assert.Empty(vm.Blocks); Assert.Empty(vm.Calendars);
        Assert.Empty(await Store.LinksAsync(originalScope));
    }
    [Fact]
    public async Task ConcurrentStageHasOneWinnerAndReceiptCannotAcknowledgeAnOldOperation()
    {
        var block = await SetupAsync();
        async Task<GoogleEventLink?> Stage()
        {
            try { return await Store.StagePublishAsync(Scope, block.Id, block.Revision, null, "google-owner", GoogleClient, CalendarId, "Work"); }
            catch (CalendarConflictException) { return null; }
        }
        var results = await Task.WhenAll(Stage(), Stage()); var pending = Assert.Single(results, row => row is not null)!;
        var done = await Store.FinishLinkAsync(pending, GoogleLinkState.Linked, "\"etag\"");
        await Assert.ThrowsAsync<CalendarConflictException>(() => Store.FinishLinkAsync(pending, GoogleLinkState.Linked, "\"stale\""));
        Assert.Equal(done, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task AccountChangeAfterProviderWriteLeavesOriginalPendingReceiptForRecovery()
    {
        var block = await SetupAsync(); var originalScope = Scope;
        provider.AfterCreate = async () => { provider.AfterCreate = null; await client.Session.SignOutAsync(); };
        await Assert.ThrowsAnyAsync<Exception>(() => Service.PublishAsync(originalScope, block, Calendar, null, CancellationToken.None));
        var pending = Assert.Single(await Store.LinksAsync(originalScope)); Assert.Equal(GoogleLinkState.PendingCreate, pending.State);
        Assert.Single(provider.Events); await client.LoginAsync();
        Assert.Equal(GoogleLinkState.Linked, (await Service.RetryAsync(pending, CancellationToken.None)).State); Assert.Equal(1, provider.Creates);
    }
    [Fact]
    public async Task Delete412RetainsProviderEventAndFreezesTheLink()
    {
        var block = await SetupAsync(); var link = await PublishAsync(block); provider.PreconditionFailure = true;
        var result = await Service.DeleteAsync(link, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Conflict, result.State); Assert.Single(provider.Events); Assert.Equal(0, provider.Deletes);
    }
    [Fact]
    public async Task RepeatedCalendarPageTokenFailsWithoutReturningPartialDestinationChoices()
    {
        await SetupAsync(); provider.RepeatPages = true;
        await Assert.ThrowsAsync<GoogleEventConflictException>(() => Service.CalendarsAsync(Scope, CancellationToken.None));
        Assert.Equal(2, provider.Requests.Count); Assert.Empty(provider.Events);
    }
    private sealed class Vault : IGoogleTokenVault
    {
        public Dictionary<string, string> Values { get; } = new();
        public Task<string?> ReadAsync(string scope) => Task.FromResult(Values.GetValueOrDefault(scope));
        public Task WriteAsync(string scope, string json) { Values[scope] = json; return Task.CompletedTask; }
        public void Block(string scope) => Values.Remove(scope);
    }
    private sealed class Broker : IGoogleAuthorizationBroker
    {
        public bool IsSupported => true;
        public Task<Uri> AuthorizeAsync(Func<Uri, Uri> authorizationUrl, string state, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed record Request(HttpMethod Method, Uri Uri, string? Token, string? ETag, string Body);
    private sealed class Provider : HttpMessageHandler
    {
        public Dictionary<string, JsonObject> Events { get; } = new();
        public List<Request> Requests { get; } = new();
        public bool LoseCreate, LosePatch, LoseDelete, PreconditionFailure, HideNextGet, RepeatPages;
        public Func<Task>? AfterCreate;
        public bool Owner = true;
        public int Creates, Patches, Deletes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var etag = request.Headers.IfMatch.FirstOrDefault()?.ToString();
            Requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter, etag, body));
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            if (path.EndsWith("/calendarList")) return ClientHarness.Json(new { nextPageToken = RepeatPages ? "repeat" : null, items = new[] {
                new { id = CalendarId, summary = "Work", accessRole = "owner" }, new { id = "read-only", summary = "Read", accessRole = "reader" } } });
            if (path.Contains("/calendarList/")) return ClientHarness.Json(new { id = path[(path.LastIndexOf('/') + 1)..], summary = "Work", accessRole = Owner ? "owner" : "reader" });
            if (request.Method == HttpMethod.Post)
            {
                var data = JsonNode.Parse(body)!.AsObject(); var id = data["id"]!.GetValue<string>();
                if (Events.ContainsKey(id)) return new(HttpStatusCode.Conflict);
                data["etag"] = "\"v1\""; data["status"] = "confirmed"; Events[id] = data; Creates++;
                if (AfterCreate is not null) await AfterCreate();
                if (LoseCreate) { LoseCreate = false; throw new HttpRequestException("Fake create response lost"); }
                return ClientHarness.Json(data);
            }
            var eventId = path[(path.LastIndexOf('/') + 1)..];
            if (!Events.TryGetValue(eventId, out var item)) return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get)
            {
                if (HideNextGet) { HideNextGet = false; return new(HttpStatusCode.NotFound); }
                return ClientHarness.Json(item);
            }
            if (PreconditionFailure || etag != item["etag"]!.GetValue<string>()) return new(HttpStatusCode.PreconditionFailed);
            if (request.Method == HttpMethod.Patch)
            {
                var update = JsonNode.Parse(body)!.AsObject(); foreach (var field in update) item[field.Key] = field.Value?.DeepClone();
                item["etag"] = $"\"v{Patches + 2}\""; Patches++;
                if (LosePatch) { LosePatch = false; throw new HttpRequestException("Fake patch response lost"); }
                return ClientHarness.Json(item);
            }
            if (request.Method == HttpMethod.Delete)
            {
                Events.Remove(eventId); Deletes++;
                if (LoseDelete) { LoseDelete = false; throw new HttpRequestException("Fake delete response lost"); }
                return new(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException("Unexpected fake Google route");
        }
    }
}
