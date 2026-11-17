using TodoApp.Domain.Calendar;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Tests;

public sealed class ConsumerWorkspaceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TodoAppUx-" + Guid.NewGuid().ToString("N"));
    private string File(string name) { Directory.CreateDirectory(directory); return Path.Combine(directory, name); }
    [Fact]
    public async Task DetailsShowRealRelationshipsAndCreateSubtaskWithoutChangingParent()
    {
        using var client = new ClientHarness();
        var device = new SqliteLocalTaskService(File("device.db")); var sync = new SqliteSyncStore(File("sync.db"));
        var parent = await device.CreateAsync(new("Prepare presentation", Priority: TaskPriority.High));
        using var model = new TaskDetailsViewModel(new(device, sync, client.Session), device, sync, client.Session);
        await model.LoadAsync(new(HierarchyWorkspace.Device, parent.Id, client.Session.Version));
        model.SubtaskTitle = "Build slides"; await model.AddSubtaskAsync();
        Assert.Equal("Build slides", Assert.Single(model.Subtasks).Title);
        Assert.Equal(parent.Id, (await device.GetTasksAsync()).Single(t => t.Title == "Build slides").ParentTaskId);
        Assert.Equal(TaskPriority.High, (await device.GetByIdAsync(parent.Id))!.Priority);
        Assert.Empty((await sync.ReadAsync("unused")).Rows);
    }
    [Fact]
    public async Task IdenticalTaskIdsRemainSeparateAcrossWorkspacesAndAccountChangesInvalidateDetails()
    {
        using var client = new ClientHarness(); await client.LoginAsync();
        var device = new SqliteLocalTaskService(File("device.db")); var sync = new SqliteSyncStore(File("sync.db"));
        var local = await device.CreateAsync(new("Device-only title"));
        var reader = new WorkspaceTaskReader(device, sync, client.Session);
        var partition = reader.Scope(HierarchyWorkspace.SyncedAccount, client.Session.Version);
        await sync.QueueAsync(partition, local.Id, new SyncTaskData("Account title"));
        using var model = new TaskDetailsViewModel(reader, device, sync, client.Session); model.WatchSession();
        var context = new TaskContext(HierarchyWorkspace.SyncedAccount, local.Id, client.Session.Version);
        await model.LoadAsync(context); Assert.Equal("Account title", model.Title);
        model.SubtaskTitle = "Account child"; await model.AddSubtaskAsync();
        Assert.Equal(2, (await sync.ReadAsync(partition)).Rows.Count);
        Assert.Single(await device.GetTasksAsync());
        await client.Session.SignOutAsync();
        Assert.False(model.CanEdit); Assert.Empty(model.Subtasks); Assert.Equal("", model.Parent);
        await Assert.ThrowsAsync<ApiClientException>(() => reader.ReadAsync(HierarchyWorkspace.SyncedAccount, context.SessionVersion));
        await model.AddSubtaskAsync(); Assert.Equal(2, (await sync.ReadAsync(partition)).Rows.Count);
    }
    [Fact]
    public async Task TodayUsesWorkspaceDatesBlocksAndHidesCompletedTasksWithoutNetworkOrPermissions()
    {
        using var client = new ClientHarness();
        var device = new SqliteLocalTaskService(File("device.db")); var sync = new SqliteSyncStore(File("sync.db"));
        var clock = new ClientClock(); var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var due = await device.CreateAsync(new("Today", DueDate: today));
        await device.CreateAsync(new("Overdue", DueDate: today.AddDays(-1)));
        var completed = await device.CreateAsync(new("Done", DueDate: today)); await device.SetCompletedAsync(completed.Id, true);
        var calendar = new SqliteCalendarStore(File("calendar.db"));
        await calendar.SaveAsync("device", Guid.NewGuid(), new TimeBlockDraft(due.Id, due.Title, today.ToDateTime(new TimeOnly(9,0)), TimeZoneInfo.Local.Id, 30), null);
        var platform = new FakeReminderPlatform();
        var reminders = new ReminderService(new SqliteReminderStore(File("reminders.db")), new(device, sync, client.Session), platform, client.Session, clock);
        using var model = new TodayViewModel(new(device, sync, client.Session), calendar, reminders, client.Session, clock);
        await model.RefreshAsync();
        Assert.Equal("Today", Assert.Single(model.Due).Title); Assert.Equal("Overdue", Assert.Single(model.Overdue).Title);
        Assert.Single(model.Scheduled); Assert.Empty(client.Handler.Requests);
        model.Workspace = HierarchyWorkspace.SyncedAccount; await model.RefreshAsync();
        Assert.Empty(model.Due); Assert.Empty(model.Scheduled); Assert.NotEmpty(model.Error);
    }
    [Fact]
    public async Task NormalEditorPersistsExplicitClassificationAndRetainsConfiguredOrigin()
    {
        using var client = new ClientHarness(); client.Settings.ApiBaseUrl = "https://paja2025.tail0aad07.ts.net/";
        var device = new SqliteLocalTaskService(File("device.db"));
        var editor = new TaskEditorViewModel(device, new ClientNavigation()); await editor.LoadAsync(null);
        editor.Title = "Important task"; editor.SelectedQuadrant = editor.Quadrants.First(q => q.Value == EisenhowerQuadrant.ImportantNotUrgent);
        await editor.SaveCommand.ExecuteAsync();
        Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent, Assert.Single(await device.GetTasksAsync()).Quadrant);
        Assert.Equal("https://paja2025.tail0aad07.ts.net/", client.Settings.ApiBaseUrl);
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath());
        if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("TodoAppUx-") && Directory.Exists(full)) Directory.Delete(full, true);
    }
}
