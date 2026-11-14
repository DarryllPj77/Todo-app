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
    private void ChangeRemote(GoogleEventLink link, string title = "Google revision", DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var item = provider.Events[link.EventId]; item["summary"] = title; item["etag"] = "\"" + Guid.NewGuid().ToString("N") + "\"";
        if (start is { } from) item["start"] = new JsonObject { ["dateTime"] = from.ToString("O") };
        if (end is { } until) item["end"] = new JsonObject { ["dateTime"] = until.ToString("O") };
    }
    [Fact]
    public async Task ReviewUpgradeFromSchemaTwoPreservesExistingBlocksAndLinks()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block);
        using (var sql = new SqliteConnection($"Data Source={Path.Combine(directory, "calendar.db3")};Pooling=False"))
        {
            await sql.OpenAsync(); using var command = sql.CreateCommand(); command.CommandText = "PRAGMA user_version=2;"; await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope))); Assert.Equal(linked, Assert.Single(await Store.LinksAsync(Scope)));
        using var check = new SqliteConnection($"Data Source={Path.Combine(directory, "calendar.db3")};Pooling=False"); await check.OpenAsync();
        using var version = check.CreateCommand(); version.CommandText = "PRAGMA user_version;"; Assert.Equal(4L, await version.ExecuteScalarAsync());
    }
    [Fact]
    public async Task ReviewPersistsSideBySideSnapshotWithoutChangingEitherVersion()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); ChangeRemote(linked);
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        Assert.Equal(GoogleReviewKind.Present, reviewed.Review?.Kind); Assert.Equal(block, reviewed.Review?.Local);
        Assert.Equal("Google revision", reviewed.Review?.Remote?.Title); Assert.Equal(linked.State, reviewed.State);
        Assert.Equal(linked.Published, reviewed.Published); Assert.Equal(0, provider.Patches); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
        Assert.Equal(reviewed, Assert.Single(await Store.LinksAsync(Scope))); Assert.Contains("Google revision", reviewed.Review!.Details);
    }
    [Fact]
    public async Task ReviewedGoogleFieldsUpdateOnlyBlockAndAcknowledgedBaseline()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block);
        ChangeRemote(linked, "Moved in Google", block.StartUtc.AddHours(1), block.EndUtc.AddHours(1));
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        var done = await Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None);
        var adopted = Assert.Single(await Store.ReadAsync(Scope));
        Assert.Equal(block.TaskId, adopted.TaskId); Assert.Equal(block.Scope, adopted.Scope); Assert.Equal(block.TimeZoneId, adopted.TimeZoneId);
        Assert.Equal("Moved in Google", adopted.Title); Assert.Equal(block.StartUtc.AddHours(1), adopted.StartUtc); Assert.Equal(block.Revision + 1, adopted.Revision);
        Assert.Equal(CalendarBlockSnapshot.From(adopted), done.Published); Assert.Null(done.Review); Assert.Equal(GoogleLinkState.Linked, done.State);
        Assert.Equal(reviewed.Review!.Remote!.ETag, done.ETag); Assert.Equal(0, provider.Patches); Assert.Equal(0, provider.Deletes);
    }
    [Fact]
    public async Task ReviewAdoptionOverlapRollsBackBothBlockAndLink()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block);
        await Store.SaveAsync(Scope, Guid.NewGuid(), new(Guid.NewGuid(), "Another block", block.WallStart.AddHours(1), "UTC", 30), null);
        ChangeRemote(linked, start: block.StartUtc.AddHours(1), end: block.EndUtc.AddHours(1));
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None));
        Assert.Equal(block, (await Store.ReadAsync(Scope)).Single(b => b.Id == block.Id)); Assert.Equal(reviewed, Assert.Single(await Store.LinksAsync(Scope)));
        Assert.Equal(0, provider.Patches);
    }
    [Theory]
    [InlineData("long-title")]
    [InlineData("whitespace")]
    [InlineData("seconds")]
    [InlineData("duration")]
    public async Task ReviewRejectsUnrepresentableAdoptionButCanPushValidLocalFields(string invalid)
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block);
        ChangeRemote(linked, invalid == "long-title" ? new string('x', 201) : invalid == "whitespace" ? " leading space " : "Google revision",
            invalid == "seconds" ? block.StartUtc.AddSeconds(30) : null, invalid == "duration" ? block.StartUtc.AddHours(25) : null);
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None));
        var resolved = await Service.ResolveAsync(reviewed, GoogleReviewChoice.KeepLocal, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Linked, resolved.State); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope))); Assert.Equal(1, provider.Patches);
    }
    [Theory]
    [InlineData(GoogleReviewChoice.UseGoogle)]
    [InlineData(GoogleReviewChoice.KeepLocal)]
    public async Task ProviderChangeAfterReviewRejectsTheDisplayedChoice(GoogleReviewChoice choice)
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); ChangeRemote(linked);
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); ChangeRemote(linked, "Changed again");
        await Assert.ThrowsAsync<GoogleEventConflictException>(() => Service.ResolveAsync(reviewed, choice, CancellationToken.None));
        Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope))); Assert.Equal(0, provider.Patches); Assert.Equal(reviewed, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Theory]
    [InlineData(GoogleReviewChoice.UseGoogle)]
    [InlineData(GoogleReviewChoice.KeepLocal)]
    public async Task LocalChangeAfterReviewRejectsResolutionInsteadOfOverwritingNewDraft(GoogleReviewChoice choice)
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); ChangeRemote(linked);
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); var newer = await RescheduleAsync(block, 60);
        await Assert.ThrowsAsync<CalendarConflictException>(() => Service.ResolveAsync(reviewed, choice, CancellationToken.None));
        Assert.Equal(newer, Assert.Single(await Store.ReadAsync(Scope))); Assert.Equal(0, provider.Patches);
    }
    [Fact]
    public async Task ReplacedReviewCannotBeUsedByAnotherWindow()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block);
        var first = await Service.ReviewAsync(linked, CancellationToken.None); var second = await Service.ReviewAsync(first, CancellationToken.None);
        await Assert.ThrowsAsync<CalendarConflictException>(() => Service.ResolveAsync(first, GoogleReviewChoice.UseGoogle, CancellationToken.None));
        Assert.NotEqual(first.Review!.Id, second.Review!.Id); Assert.Equal(second, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task KeepLocalUsesReviewedEtagAndKeepsBlockRevisionUnchanged()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); var local = await RescheduleAsync(block);
        ChangeRemote(linked); var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        var done = await Service.ResolveAsync(reviewed, GoogleReviewChoice.KeepLocal, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Linked, done.State); Assert.Equal(local, Assert.Single(await Store.ReadAsync(Scope)));
        Assert.Equal(reviewed.Review!.Remote!.ETag, Assert.Single(provider.Requests, r => r.Method == HttpMethod.Patch).ETag);
        Assert.Equal(CalendarBlockSnapshot.From(local), done.Published);
    }
    [Fact]
    public async Task LostReviewedPushReplyRetainsSnapshotAndRecoversWithExistingRetry()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); var local = await RescheduleAsync(block);
        ChangeRemote(linked); var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); provider.LosePatch = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => Service.ResolveAsync(reviewed, GoogleReviewChoice.KeepLocal, CancellationToken.None));
        var pending = Assert.Single(await Store.LinksAsync(Scope)); Assert.Equal(GoogleLinkState.PendingUpdate, pending.State); Assert.Null(pending.Review);
        await RescheduleAsync(local, 60); var done = await Service.RetryAsync(pending, CancellationToken.None);
        Assert.Equal(local.EndUtc, done.Published?.EndUtc); Assert.Equal(1, provider.Patches);
    }
    [Fact]
    public async Task ReviewedPushStillHonorsProvider412()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); ChangeRemote(linked);
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); provider.PreconditionFailure = true;
        var done = await Service.ResolveAsync(reviewed, GoogleReviewChoice.KeepLocal, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Conflict, done.State); Assert.Null(done.Review); Assert.Equal(0, provider.Patches);
    }
    [Fact]
    public async Task ReviewedDeletionRetainsLocalBlockAndRequiresNewExplicitPublish()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); provider.Events.Remove(linked.EventId);
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); Assert.Equal(GoogleReviewKind.Missing, reviewed.Review?.Kind);
        var deleted = await Service.ResolveAsync(reviewed, GoogleReviewChoice.AcceptMissing, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Deleted, deleted.State); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
        Assert.Empty(provider.Events); Assert.Equal(0, provider.Deletes);
        var republished = await PublishAsync(block, deleted.Revision); Assert.NotEqual(linked.EventId, republished.EventId);
    }
    [Fact]
    public async Task ReappearingEventRejectsAnOldMissingReview()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); var saved = provider.Events[linked.EventId].DeepClone().AsObject();
        provider.Events.Remove(linked.EventId); var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); provider.Events[linked.EventId] = saved;
        await Assert.ThrowsAsync<GoogleEventConflictException>(() => Service.ResolveAsync(reviewed, GoogleReviewChoice.AcceptMissing, CancellationToken.None));
        Assert.Equal(reviewed, Assert.Single(await Store.LinksAsync(Scope))); Assert.Single(provider.Events);
    }
    [Fact]
    public async Task UnsafeReviewAllowsLocalDetachWithoutGoogleCredentialsOrRemoteMutation()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); provider.Events[linked.EventId]["extendedProperties"]!["private"]!["todoProof"] = "foreign";
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); Assert.Equal(GoogleReviewKind.Unsafe, reviewed.Review?.Kind);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None));
        var protectedGrant = vault.Values[Scope]; vault.Values.Clear(); var calls = provider.Requests.Count; var oauthCalls = oauthHandler.Requests.Count;
        var detached = await Service.DetachAsync(reviewed, CancellationToken.None);
        Assert.Equal(GoogleLinkState.Detached, detached.State); Assert.Equal(calls, provider.Requests.Count); Assert.Equal(oauthCalls, oauthHandler.Requests.Count);
        Assert.Single(provider.Events); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
        vault.Values[Scope] = protectedGrant; var newLink = await PublishAsync(block, detached.Revision);
        Assert.NotEqual(linked.EventId, newLink.EventId); Assert.Equal(2, provider.Events.Count); // Explicit new publish leaves detached copy intact.
    }
    [Fact]
    public async Task OrphanGoogleAdoptionAcknowledgesLinkWithoutRecreatingLocalBlock()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); await Store.DeleteAsync(Scope, block.Id, block.Revision); ChangeRemote(linked);
        var reviewed = await Service.ReviewAsync(linked, CancellationToken.None); Assert.Null(reviewed.Review?.Local);
        var done = await Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None);
        Assert.Empty(await Store.ReadAsync(Scope)); Assert.Equal(GoogleLinkState.Linked, done.State);
        Assert.Equal(GoogleLinkState.Deleted, (await Service.DeleteAsync(done, CancellationToken.None)).State);
    }
    [Fact]
    public async Task BlockDeletedAfterReviewIsNotResurrectedByAdoption()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        await Store.DeleteAsync(Scope, block.Id, block.Revision);
        await Assert.ThrowsAsync<CalendarConflictException>(() => Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None));
        Assert.Empty(await Store.ReadAsync(Scope));
    }
    [Fact]
    public async Task ReviewAdoptionPreservesExactUtcAcrossRepeatedLocalDstTime()
    {
        var initial = await SetupAsync(); var block = await Store.SaveAsync(Scope, initial.Id,
            new(initial.TaskId, initial.Title, new DateTime(2026, 11, 1, 0, 30, 0), "America/New_York", 30), initial.Revision);
        var linked = await PublishAsync(block); var start = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
        ChangeRemote(linked, start: start, end: start.AddMinutes(30)); var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        await Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None);
        var adopted = Assert.Single(await Store.ReadAsync(Scope)); Assert.Equal(start, adopted.StartUtc);
        Assert.True(TimeZoneInfo.FindSystemTimeZoneById(adopted.TimeZoneId).IsAmbiguousTime(adopted.WallStart));
        Assert.Contains("DTSTART:20261101T053000Z", CalendarFile.Export([adopted]));
    }
    [Fact]
    public async Task ReviewResolutionConfirmationAndLogoutCannotApplyUnapprovedChoice()
    {
        var block = await SetupAsync(); var linked = await PublishAsync(block); ChangeRemote(linked);
        var navigation = new ClientNavigation(); using var vm = new GoogleEventsViewModel(client.Session, Store, Service, navigation, client.Clock);
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedLink = Assert.Single(vm.Links); await vm.ReviewCommand.ExecuteAsync();
        Assert.Contains("Google revision", vm.ReviewDetails); await vm.UseGoogleCommand.ExecuteAsync(); Assert.Equal(block, Assert.Single(await Store.ReadAsync(Scope)));
        var scope = Scope; navigation.GoogleEventConfirmation = async () => { await client.Session.SignOutAsync(); return true; };
        await vm.UseGoogleCommand.ExecuteAsync(); Assert.Empty(vm.Links); Assert.DoesNotContain("Google revision", vm.ReviewDetails);
        Assert.Equal(block, Assert.Single(await Store.ReadAsync(scope))); Assert.Equal(0, provider.Patches);
    }
    [Fact]
    public async Task PendingOperationCannotBeDetachedOrReviewedBeforeRecovery()
    {
        var block = await SetupAsync(); provider.LoseCreate = true; await Assert.ThrowsAsync<HttpRequestException>(() => PublishAsync(block));
        var pending = Assert.Single(await Store.LinksAsync(Scope));
        await Assert.ThrowsAsync<ArgumentException>(() => Service.ReviewAsync(pending, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Service.DetachAsync(pending, CancellationToken.None));
        Assert.Equal(pending, Assert.Single(await Store.LinksAsync(Scope)));
    }
    [Fact]
    public async Task AdoptionUsesOriginalLocalYearBoundsRatherThanUtcYear()
    {
        var initial = await SetupAsync(); var block = await Store.SaveAsync(Scope, initial.Id,
            new(initial.TaskId, initial.Title, new DateTime(2000, 1, 1, 0, 30, 0), "Pacific/Kiritimati", 30), initial.Revision);
        Assert.Equal(1999, block.StartUtc.Year);
        var linked = await PublishAsync(block); var reviewed = await Service.ReviewAsync(linked, CancellationToken.None);
        await Service.ResolveAsync(reviewed, GoogleReviewChoice.UseGoogle, CancellationToken.None);
        var adopted = Assert.Single(await Store.ReadAsync(Scope)); Assert.Equal(block.StartUtc, adopted.StartUtc); Assert.Equal(2000, adopted.WallStart.Year);
    }
}
