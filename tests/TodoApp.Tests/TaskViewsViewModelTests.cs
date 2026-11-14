using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Domain.Views;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class TaskViewsViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "todoapp_view_vm_" + Guid.NewGuid().ToString("N"));
    private readonly ClientHarness client = new();
    private readonly InMemoryLocalTaskService local = new();
    private readonly ViewNavigation navigation = new();
    private SqliteTaskViewStore Store => new(Path.Combine(directory, "views.db3"));
    private SqliteSyncStore Sync => new(Path.Combine(directory, "sync.db3"));
    private TaskViewsViewModel ViewModel(ILocalTaskService? source = null, TimeProvider? clock = null) =>
        new(Store, source ?? local, Sync, client.Session, navigation, clock ?? client.Clock);
    public void Dispose() { client.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    [Fact]
    public async Task DraftFilters_DoNotAffectResultsUntilApplyAndSavedRulesReopen()
    {
        await local.CreateAsync(new TaskDetails("Read code", Priority: TaskPriority.High));
        await local.CreateAsync(new TaskDetails("Other"));
        using var vm = ViewModel(); await vm.RefreshCommand.ExecuteAsync(); Assert.Equal(2, vm.Results.Count);
        vm.Search = "read"; vm.Priority = ViewPriority.High; Assert.Equal(2, vm.Results.Count);
        await vm.ApplyCommand.ExecuteAsync(); Assert.Equal("Read code", Assert.Single(vm.Results).Title);
        vm.ViewName = "Focus"; await vm.CreateCommand.ExecuteAsync(); Assert.Single(vm.SavedViews);
        using var reopened = ViewModel(); await reopened.RefreshCommand.ExecuteAsync(); Assert.Equal(2, reopened.Results.Count);
        reopened.SelectedView = Assert.Single(reopened.SavedViews); Assert.Equal(2, reopened.Results.Count);
        await reopened.ApplyCommand.ExecuteAsync(); Assert.Single(reopened.Results);
    }
    [Fact]
    public async Task UpdateRenameAndConfirmedDelete_LeaveTasksAndAppliedFiltersIntact()
    {
        await local.CreateAsync(new TaskDetails("Keep task"));
        using var vm = ViewModel(); await vm.RefreshCommand.ExecuteAsync();
        vm.ViewName = "Original"; vm.Search = "Keep"; await vm.CreateCommand.ExecuteAsync();
        var id = vm.SelectedView!.Id;
        vm.ViewName = "Renamed"; await vm.UpdateCommand.ExecuteAsync();
        Assert.Equal(id, vm.SelectedView!.Id); Assert.Equal("Renamed", vm.SelectedView.Name);
        navigation.Confirm = false; await vm.DeleteCommand.ExecuteAsync(); Assert.Single(vm.SavedViews);
        navigation.Confirm = true; await vm.DeleteCommand.ExecuteAsync(); Assert.Empty(vm.SavedViews);
        Assert.Single(vm.Results); Assert.Single(await local.GetTasksAsync()); Assert.Equal("Keep", vm.Search);
    }
    [Fact]
    public async Task DuplicateNameFailure_PreservesDraftAndExistingDefinition()
    {
        using var vm = ViewModel(); await vm.RefreshCommand.ExecuteAsync(); vm.ViewName = "Focus"; await vm.CreateCommand.ExecuteAsync();
        vm.Search = "unsaved draft"; await vm.CreateCommand.ExecuteAsync();
        Assert.Contains("already exists", vm.Error); Assert.Equal("unsaved draft", vm.Search);
        Assert.Equal("", Assert.Single(await Store.ReadAsync("device")).Definition.Search);
    }
    [Fact]
    public async Task ConcurrentWindowEdit_RequiresReviewInsteadOfOverwriting()
    {
        await Store.CreateAsync("device", "Shared", new());
        using var first = ViewModel(); using var second = ViewModel();
        await first.RefreshCommand.ExecuteAsync(); await second.RefreshCommand.ExecuteAsync();
        first.SelectedView = Assert.Single(first.SavedViews); second.SelectedView = Assert.Single(second.SavedViews);
        first.Search = "First"; await first.UpdateCommand.ExecuteAsync();
        second.Search = "Second"; await second.UpdateCommand.ExecuteAsync();
        Assert.Contains("another window", second.Error); Assert.Equal("Second", second.Search);
        Assert.Equal("First", Assert.Single(await Store.ReadAsync("device")).Definition.Search);
    }
    [Fact]
    public async Task AccountAndOriginChanges_ClearSavedNamesFiltersAndResults()
    {
        await client.LoginAsync(); var original = client.Account;
        var firstScope = SqliteSyncStore.Partition(client.Session.Current!.Origin, original.User.Id);
        await Sync.QueueAsync(firstScope, Guid.NewGuid(), new("Private task"));
        using var vm = ViewModel(); vm.Workspace = TaskViewWorkspace.SyncedAccount;
        await vm.RefreshCommand.ExecuteAsync(); vm.ViewName = "Private filter"; vm.Search = "Private"; await vm.CreateCommand.ExecuteAsync();
        Assert.Single(vm.Results); Assert.Single(vm.SavedViews);
        await client.Session.SignOutAsync();
        Assert.Empty(vm.Results); Assert.Empty(vm.SavedViews); Assert.Equal("", vm.Search); Assert.Equal("", vm.ViewName);
        client.Account = client.Auth("second-token", "second@example.invalid"); await client.LoginAsync();
        await vm.RefreshCommand.ExecuteAsync(); Assert.Empty(vm.Results); Assert.Empty(vm.SavedViews);
        client.Account = original; await client.LoginAsync(); await vm.RefreshCommand.ExecuteAsync(); Assert.Single(vm.Results); Assert.Single(vm.SavedViews);
        await client.Session.ChangeEndpointAsync("https://other.invalid/");
        Assert.Empty(vm.Results); Assert.Empty(vm.SavedViews); Assert.Single(await Store.ReadAsync(firstScope));
    }
    [Fact]
    public async Task AccountChanges_DoNotResetDeviceOnlyViewDrafts()
    {
        using var vm = ViewModel(); await vm.RefreshCommand.ExecuteAsync(); vm.Search = "device draft";
        await client.LoginAsync(); Assert.Equal("device draft", vm.Search);
        await client.Session.SignOutAsync(); Assert.Equal("device draft", vm.Search);
    }
    [Fact]
    public async Task WorkspaceSwitchDuringSlowRead_DiscardsOldRows()
    {
        await local.CreateAsync(new TaskDetails("Old source"));
        var source = new BlockingSource(local);
        using var vm = ViewModel(source);
        var reading = vm.ApplyCommand.ExecuteAsync(); await source.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.Workspace = TaskViewWorkspace.SyncedAccount;
        source.Release.SetResult(); await reading;
        Assert.Empty(vm.Results); Assert.Contains("workspace changed", vm.Error);
    }
    [Fact]
    public async Task AccountSwitchDuringDeleteConfirmation_DoesNotDeleteOldScopeDefinition()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id);
        await Store.CreateAsync(scope, "Private", new());
        using var vm = ViewModel(); vm.Workspace = TaskViewWorkspace.SyncedAccount; await vm.RefreshCommand.ExecuteAsync(); vm.SelectedView = Assert.Single(vm.SavedViews);
        navigation.Confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleting = vm.DeleteCommand.ExecuteAsync();
        await client.Session.SignOutAsync(); navigation.Confirmation.SetResult(true); await deleting;
        Assert.Single(await Store.ReadAsync(scope)); Assert.Empty(vm.SavedViews); Assert.Empty(vm.Results);
    }
    [Fact]
    public async Task ApplyingViews_DoesNotWriteTasksOrOutboxAndKeepsConflictCountsVisible()
    {
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id);
        var id = Guid.NewGuid(); var sync = Sync; await sync.QueueAsync(scope, id, new("Queued task"));
        var pending = (await sync.ReadAsync(scope)).Rows[id].Pending!;
        using var vm = ViewModel(); vm.Workspace = TaskViewWorkspace.SyncedAccount; await vm.RefreshCommand.ExecuteAsync();
        vm.Search = "nothing matches"; await vm.ApplyCommand.ExecuteAsync(); Assert.Empty(vm.Results);
        Assert.Contains("1 queued", vm.Status); Assert.Equal(pending, (await sync.ReadAsync(scope)).Rows[id].Pending);
        Assert.Empty(await local.GetTasksAsync()); Assert.Empty(await Store.ReadAsync(scope));
    }
    [Fact]
    public async Task DateRulesUseDeviceDayAndRefreshRecomputesAfterMidnight()
    {
        var clock = new ViewClock { Now = new(2026, 11, 12, 23, 30, 0, TimeSpan.Zero) };
        await local.CreateAsync(new TaskDetails("Local today", DueDate: new DateOnly(2026,11,13)));
        using var vm = ViewModel(clock: clock); vm.Due = ViewDue.Today; await vm.ApplyCommand.ExecuteAsync(); Assert.Single(vm.Results);
        clock.Now = clock.Now.AddHours(24); await vm.RefreshCommand.ExecuteAsync(); Assert.Empty(vm.Results);
        Assert.Equal(ViewDue.Today, vm.Due);
    }
    [Fact]
    public async Task SourceNavigation_IsExplicitAndDoesNotChangeTheFilter()
    {
        var task = await local.CreateAsync(new TaskDetails("Edit me")); using var vm = ViewModel(); await vm.RefreshCommand.ExecuteAsync();
        vm.SelectedTask = Assert.Single(vm.Results); await vm.OpenCommand.ExecuteAsync(); Assert.Equal(task.Id, navigation.EditedId);
        await client.LoginAsync(); var scope = SqliteSyncStore.Partition(client.Session.Current!.Origin, client.Account.User.Id);
        await Sync.QueueAsync(scope, Guid.NewGuid(), new("Cached"));
        vm.Workspace = TaskViewWorkspace.SyncedAccount; await vm.RefreshCommand.ExecuteAsync(); vm.SelectedTask = Assert.Single(vm.Results);
        await vm.OpenCommand.ExecuteAsync(); Assert.Equal(1, navigation.SyncOpened);
    }
    private sealed class ViewClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("View test +8", TimeSpan.FromHours(8), "View test +8", "View test +8");
    }
    private sealed class ViewNavigation : ITaskNavigation
    {
        public Task OpenGoogleEventsAsync() => Task.CompletedTask;
        public Task<bool> ConfirmGoogleEventAsync(string action, string details) => Task.FromResult(false);
        public Task OpenGoogleConnectionAsync() => Task.CompletedTask;
        public Task<bool> ConfirmGoogleDisconnectAsync(bool revoke) => Task.FromResult(false);
        public Task OpenCalendarAsync() => Task.CompletedTask;
        public Task<bool> ConfirmDiscardCalendarDraftAsync() => Task.FromResult(false);
        public Task<bool> ConfirmDeleteCalendarBlockAsync(string title) => Task.FromResult(false);
        public bool Confirm { get; set; }
        public Guid? EditedId { get; private set; }
        public int SyncOpened { get; private set; }
        public TaskCompletionSource<bool>? Confirmation { get; set; }
        public Task<bool> ConfirmDeleteViewAsync(string name) => Confirmation?.Task ?? Task.FromResult(Confirm);
        public Task<bool> ConfirmDeleteAsync(string name) => Task.FromResult(false);
        public Task EditTaskAsync(Guid? id) { EditedId = id; return Task.CompletedTask; }
        public Task OpenSyncAsync() { SyncOpened++; return Task.CompletedTask; }
        public Task OpenFocusAsync() => Task.CompletedTask;
    public Task OpenRecurrenceAsync() => Task.CompletedTask;
    public Task<bool> ConfirmRecurrenceConflictAsync(string title) => Task.FromResult(false);
    public Task<bool> ConfirmOccurrenceAsync(string title, DateOnly current, DateOnly next) => Task.FromResult(false);
    public Task OpenPlanningAsync() => Task.CompletedTask;
    public Task<bool> ConfirmPlanningConflictAsync(string title) => Task.FromResult(false);
    public Task OpenDependenciesAsync() => Task.CompletedTask;
    public Task<bool> ConfirmDependencyConflictAsync(string title) => Task.FromResult(false);
    public Task OpenHierarchyAsync() => Task.CompletedTask;
        public Task<bool> ConfirmHierarchyConflictAsync(string title) => Task.FromResult(false);
        public Task OpenTaskViewsAsync() => Task.CompletedTask;
        public Task OpenRemindersAsync() => Task.CompletedTask;
        public Task OpenAccountAsync() => Task.CompletedTask;
        public Task CloseEditorAsync() => Task.CompletedTask;
    }
    private sealed class BlockingSource(ILocalTaskService inner) : ILocalTaskService
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter = TaskFilter.All, CancellationToken token = default)
        { Arrived.TrySetResult(); await Release.Task; return await inner.GetTasksAsync(filter, token); }
        public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken token = default) => inner.GetByIdAsync(id, token);
        public Task<TaskItem> CreateAsync(TaskDetails details, CancellationToken token = default) => inner.CreateAsync(details, token);
        public Task<bool> UpdateAsync(Guid id, TaskDetails details, CancellationToken token = default) => inner.UpdateAsync(id, details, token);
        public Task<bool> SetCompletedAsync(Guid id, bool completed, CancellationToken token = default) => inner.SetCompletedAsync(id, completed, token);
        public Task<TodoApp.Domain.Entities.TaskItem?> SetRecurrenceRuleAsync(Guid id, TodoApp.Domain.Recurrence.RepeatRule? rule, CancellationToken token = default) => inner.SetRecurrenceRuleAsync(id, rule, token);
        public Task<TodoApp.Domain.Entities.TaskItem?> CompleteOccurrenceAsync(Guid id, TodoApp.Domain.Recurrence.RepeatState expected, CancellationToken token = default) => inner.CompleteOccurrenceAsync(id, expected, token);
        public Task<bool> SetQuadrantAsync(Guid id, TodoApp.Domain.Enums.EisenhowerQuadrant quadrant, CancellationToken token = default) => inner.SetQuadrantAsync(id, quadrant, token);
        public Task<bool> SetPrerequisitesAsync(Guid id, TodoApp.Domain.Relationships.TaskPrerequisites prerequisites, CancellationToken token = default) => inner.SetPrerequisitesAsync(id, prerequisites, token);
        public Task<bool> SetParentAsync(Guid id, Guid? parentId, CancellationToken token = default) => inner.SetParentAsync(id, parentId, token);
        public Task<bool> DeleteAsync(Guid id, CancellationToken token = default) => inner.DeleteAsync(id, token);
    }
}
