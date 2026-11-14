using System.Text;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Calendar;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class CalendarTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_calendar_" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(directory, name);
    private SqliteCalendarStore Store => new(PathFor("calendar.db3"));
    private readonly InMemoryLocalTaskService device = new();
    private readonly ClientHarness client = new();
    private readonly ClientNavigation navigation = new();
    private readonly Exporter exporter = new();
    private SqliteSyncStore Sync => new(PathFor("sync.db3"));
    private CalendarViewModel Model() => new(device, Sync, Store, client.Session, navigation, exporter, client.Clock);
    private static TimeBlockDraft Draft(int hour = 9, int duration = 30) => new(Guid.NewGuid(), "Review", new DateTime(2026, 11, 15, hour, 0, 0), "UTC", duration);
    private async Task<TimeBlock> CreateAsync(string scope = "device", int hour = 9) => await Store.SaveAsync(scope, Guid.NewGuid(), Draft(hour), null);
    public void Dispose() { client.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void InvalidDurationsAreRejected(int duration) => Assert.Throws<ArgumentException>(() =>
        TimeBlockRules.Create(Guid.NewGuid(), "device", Draft(duration: duration), 1, DateTimeOffset.UtcNow));

    [Fact]
    public void InvalidZoneAndSubminuteStartsAreRejected()
    {
        var draft = Draft();
        Assert.Throws<ArgumentException>(() => TimeBlockRules.Create(Guid.NewGuid(), "device", draft with { TimeZoneId = "Not a zone" }, 1, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => TimeBlockRules.Create(Guid.NewGuid(), "device", draft with { WallStart = draft.WallStart.AddSeconds(1) }, 1, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(3, 8, 2, 30)]
    [InlineData(11, 1, 1, 30)]
    public void DaylightSavingGapAndRepeatedStartAreRejected(int month, int day, int hour, int minute)
    {
        var draft = Draft() with { WallStart = new DateTime(2026, month, day, hour, minute, 0), TimeZoneId = "America/New_York" };
        Assert.Throws<ArgumentException>(() => TimeBlockRules.Create(Guid.NewGuid(), "device", draft, 1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DurationAcrossDaylightSavingUsesElapsedMinutes()
    {
        var draft = Draft(duration: 120) with { WallStart = new DateTime(2026, 3, 8, 1, 30, 0), TimeZoneId = "America/New_York" };
        var block = TimeBlockRules.Create(Guid.NewGuid(), "device", draft, 1, DateTimeOffset.UtcNow);
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero), block.StartUtc);
        Assert.Equal(TimeSpan.FromHours(2), block.EndUtc - block.StartUtc);
        Assert.Equal(4, TimeZoneInfo.ConvertTime(block.EndUtc, TimeZoneInfo.FindSystemTimeZoneById(draft.TimeZoneId)).Hour);
    }

    [Fact]
    public async Task ReopenRescheduleDeletePreserveIdentityAndRejectStaleWindows()
    {
        var saved = await CreateAsync();
        Assert.Equal(saved, Assert.Single(await Store.ReadAsync("device")));
        var draft = new TimeBlockDraft(saved.TaskId, saved.Title, saved.WallStart.AddHours(1), saved.TimeZoneId, 45);
        var updated = await Store.SaveAsync("device", saved.Id, draft, saved.Revision);
        Assert.Equal(saved.Id, updated.Id); Assert.Equal(2, updated.Revision);
        await Assert.ThrowsAsync<CalendarConflictException>(() => Store.SaveAsync("device", saved.Id, draft, saved.Revision));
        await Assert.ThrowsAsync<CalendarConflictException>(() => Store.DeleteAsync("device", saved.Id, saved.Revision));
        Assert.Equal(updated, Assert.Single(await Store.ReadAsync("device")));
        await Store.DeleteAsync("device", updated.Id, updated.Revision);
        Assert.Empty(await Store.ReadAsync("device"));
    }

    [Fact]
    public async Task OverlapIsTransactionalAndAdjacentBlocksAndOtherScopesAreAllowed()
    {
        var first = await CreateAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveAsync("device", Guid.NewGuid(), Draft(duration: 60), null));
        await Store.SaveAsync("device", Guid.NewGuid(), Draft() with { WallStart = first.EndUtc.DateTime }, null);
        await CreateAsync("account-a");
        Assert.Equal(2, (await Store.ReadAsync("device")).Count);
        Assert.Single(await Store.ReadAsync("account-a"));
    }

    [Fact]
    public async Task ConcurrentOverlappingCreatesHaveOnlyOneWinner()
    {
        async Task<bool> SaveAsync()
        {
            try { await CreateAsync(); return true; }
            catch (ArgumentException) { return false; }
        }
        var results = await Task.WhenAll(SaveAsync(), SaveAsync());
        Assert.Single(results, value => value);
        Assert.Single(await Store.ReadAsync("device"));
    }

    [Fact]
    public async Task ScopeGuardsAndImmutableTaskSnapshotsPreventCrossAccountWrites()
    {
        var saved = await CreateAsync("account-a");
        var draft = new TimeBlockDraft(saved.TaskId, saved.Title, saved.WallStart, saved.TimeZoneId, 30);
        Assert.Empty(await Store.ReadAsync("account-b"));
        await Assert.ThrowsAsync<CalendarConflictException>(() => Store.SaveAsync("account-b", saved.Id, draft, saved.Revision));
        await Assert.ThrowsAsync<CalendarConflictException>(() => Store.DeleteAsync("account-b", saved.Id, saved.Revision));
        await Assert.ThrowsAsync<ArgumentException>(() => Store.SaveAsync("account-a", saved.Id, draft with { Title = "Another task" }, saved.Revision));
        Assert.Equal(saved, Assert.Single(await Store.ReadAsync("account-a")));
    }

    [Fact]
    public async Task UnsupportedSchemaAndCorruptScopeFailWithoutReset()
    {
        var saved = await CreateAsync();
        using (var connection = new SqliteConnection($"Data Source={PathFor("calendar.db3")};Pooling=False"))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99;"; await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync("device"));
        using (var connection = new SqliteConnection($"Data Source={PathFor("calendar.db3")};Pooling=False"))
        {
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=1; UPDATE CalendarBlocks SET Scope='account-b';"; await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.ReadAsync("account-b"));
    }

    [Fact]
    public async Task ExportHasStableUidSequenceUtcAndNoTaskOrAccountIdentifiers()
    {
        var saved = await CreateAsync("https://private.invalid/|account-id");
        var first = CalendarFile.Export([saved]); Assert.Equal(first, CalendarFile.Export([saved]));
        Assert.Contains($"UID:{saved.Id:N}@todoapp.local\r\n", first);
        Assert.Contains("DTSTART:20261115T090000Z\r\nDTEND:20261115T093000Z", first);
        Assert.Contains("SEQUENCE:0", first); Assert.Contains("CLASS:PRIVATE", first);
        Assert.DoesNotContain(saved.Scope, first); Assert.DoesNotContain(saved.TaskId.ToString(), first); Assert.DoesNotContain("RRULE", first);
        var updated = await Store.SaveAsync(saved.Scope, saved.Id, new(saved.TaskId, saved.Title, saved.WallStart.AddHours(1), saved.TimeZoneId, 30), 1);
        var second = CalendarFile.Export([updated]);
        Assert.Contains($"UID:{saved.Id:N}@todoapp.local", second); Assert.Contains("SEQUENCE:1", second);
        Assert.Throws<ArgumentException>(() => CalendarFile.Export([saved, updated with { Id = Guid.NewGuid(), Scope = "device" }]));
    }

    [Fact]
    public async Task ExportEscapesInjectionAndFoldsUtf8WithoutSplittingCharacters()
    {
        var saved = await CreateAsync();
        var title = "Review, notes; path\\file\r\nBEGIN:VEVENT " + string.Concat(Enumerable.Repeat("漢字🌟", 30));
        var output = CalendarFile.Export([saved with { Title = title }]);
        Assert.All(output.Split("\r\n", StringSplitOptions.RemoveEmptyEntries), line => Assert.InRange(Encoding.UTF8.GetByteCount(line), 1, 75));
        var unfolded = output.Replace("\r\n ", "");
        Assert.Contains("SUMMARY:Review\\, notes\\; path\\\\file\\nBEGIN:VEVENT", unfolded);
        Assert.Equal(1, unfolded.Split("\r\n").Count(line => line == "BEGIN:VEVENT"));
        Assert.Contains(string.Concat(Enumerable.Repeat("漢字🌟", 30)), unfolded);
        Assert.Equal(output, new UTF8Encoding(false, true).GetString(Encoding.UTF8.GetBytes(output)));
    }

    [Fact]
    public async Task ViewModelSavesReschedulesExportsAndDeletesOnlyTheBlock()
    {
        var task = await device.CreateAsync(new TaskDetails("Plan work"));
        using var vm = Model(); await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTask = Assert.Single(vm.Tasks); vm.Day = new DateTime(2026, 11, 15);
        await vm.SaveCommand.ExecuteAsync(); Assert.Equal("", vm.Error);
        var saved = Assert.Single(vm.Blocks); Assert.Equal(task.Id, saved.TaskId);
        vm.StartTime = new TimeSpan(10, 0, 0); await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(saved.Id, Assert.Single(vm.Blocks).Id); Assert.Equal(2, vm.SelectedBlock?.Revision);
        await vm.ExportCommand.ExecuteAsync(); Assert.Contains("SUMMARY:Plan work", Assert.Single(exporter.Files));
        await vm.DeleteCommand.ExecuteAsync(); Assert.Single(vm.Blocks);
        navigation.Confirm = true; await vm.DeleteCommand.ExecuteAsync(); Assert.Empty(vm.Blocks);
        Assert.False((await device.GetByIdAsync(task.Id))!.IsCompleted);
    }

    [Fact]
    public async Task DraftRefreshRequiresConfirmationAndExportRequiresSavedDraft()
    {
        await device.CreateAsync(new TaskDetails("Draft")); using var vm = Model(); await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTask = Assert.Single(vm.Tasks); vm.Duration = "45";
        await vm.RefreshCommand.ExecuteAsync(); Assert.Equal("45", vm.Duration); Assert.False(vm.CanSelectBlock);
        await vm.SaveCommand.ExecuteAsync(); vm.Duration = "60";
        await vm.ExportCommand.ExecuteAsync(); Assert.Contains("Save or refresh", vm.Error); Assert.Empty(exporter.Files);
        navigation.Confirm = true; await vm.RefreshCommand.ExecuteAsync();
        Assert.Equal("45", vm.Duration); Assert.True(vm.CanSelectBlock);
    }

    [Fact]
    public async Task TaskDeletionBeforeSaveIsRejectedButSavedSnapshotSurvivesDeletion()
    {
        var task = await device.CreateAsync(new TaskDetails("Snapshot")); using var vm = Model(); await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTask = Assert.Single(vm.Tasks); await device.DeleteAsync(task.Id); await vm.SaveCommand.ExecuteAsync();
        Assert.Contains("missing", vm.Error); Assert.Empty(vm.Blocks);
        var second = await device.CreateAsync(new TaskDetails("Keep snapshot")); navigation.Confirm = true;
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = Assert.Single(vm.Tasks); await vm.SaveCommand.ExecuteAsync();
        await device.DeleteAsync(second.Id); await vm.RefreshCommand.ExecuteAsync();
        Assert.Empty(vm.Tasks); Assert.Equal("Keep snapshot", vm.SelectedBlock?.Title);
        vm.Duration = "45"; await vm.SaveCommand.ExecuteAsync(); Assert.Equal("", vm.Error);
    }

    [Fact]
    public async Task AccountLogoutDuringDeleteConfirmationCannotRemovePriorScopeData()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Session.Current.User.Id);
        var saved = await CreateAsync(scope); using var vm = Model(); vm.Workspace = HierarchyWorkspace.SyncedAccount;
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedBlock = Assert.Single(vm.Blocks);
        navigation.CalendarConfirmation = async () => { await client.Session.SignOutAsync(); return true; };
        await vm.DeleteCommand.ExecuteAsync(); Assert.Empty(vm.Blocks); Assert.Null(vm.SelectedBlock);
        Assert.Single(await Store.ReadAsync(scope)); Assert.NotEqual("", vm.Error);
    }

    [Fact]
    public async Task AccountExpiryBlocksExportAndClearsPrivateRows()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Session.Current.User.Id);
        await CreateAsync(scope); using var vm = Model(); vm.Workspace = HierarchyWorkspace.SyncedAccount;
        await vm.RefreshCommand.ExecuteAsync(); vm.SelectedBlock = Assert.Single(vm.Blocks);
        client.Clock.Now = client.Session.Current.ExpiresAt.AddSeconds(1);
        await vm.ExportCommand.ExecuteAsync(); Assert.Empty(exporter.Files); Assert.Empty(vm.Blocks); Assert.Null(vm.SelectedBlock);
    }

    [Fact]
    public async Task AccountCacheCreatesLocalBlockWithoutAddingSyncMutations()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Session.Current.User.Id);
        var id = Guid.NewGuid();
        await Sync.QueueAsync(scope, id, new SyncTaskData("Account task", null, null, TaskPriority.Normal, null, false));
        var before = (await Sync.ReadAsync(scope)).Rows[id].Pending;
        using var vm = Model(); vm.Workspace = HierarchyWorkspace.SyncedAccount; await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTask = Assert.Single(vm.Tasks); await vm.SaveCommand.ExecuteAsync();
        Assert.Equal(scope, Assert.Single(vm.Blocks).Scope); Assert.Equal(before, (await Sync.ReadAsync(scope)).Rows[id].Pending);
        vm.Workspace = HierarchyWorkspace.Device; await vm.RefreshCommand.ExecuteAsync(); Assert.Empty(vm.Blocks); Assert.Empty(vm.Tasks);
    }

    [Fact]
    public async Task StaleSelectionCannotExportAnUnreviewedRevision()
    {
        var saved = await CreateAsync(); using var vm = Model(); await vm.RefreshCommand.ExecuteAsync(); vm.SelectedBlock = Assert.Single(vm.Blocks);
        await Store.SaveAsync("device", saved.Id, new(saved.TaskId, saved.Title, saved.WallStart.AddHours(1), saved.TimeZoneId, 30), saved.Revision);
        await vm.ExportCommand.ExecuteAsync(); Assert.Empty(exporter.Files); Assert.Contains("another window", vm.Error);
    }

    private sealed class Exporter : ICalendarFileExporter
    {
        public List<string> Files { get; } = new();
        public Task ShareAsync(string calendar, Action ensureAccess) { ensureAccess(); Files.Add(calendar); return Task.CompletedTask; }
    }
}
