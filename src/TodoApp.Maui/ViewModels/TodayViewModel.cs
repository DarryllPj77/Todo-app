using System.Collections.ObjectModel;
using TodoApp.Domain.Calendar;
using TodoApp.Domain.Reminders;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Maui.ViewModels;

public sealed class TodayViewModel(WorkspaceTaskReader reader, SqliteCalendarStore calendar,
    ReminderService reminders, ClientSession session, TimeProvider? time = null) : ObservableViewModel, IDisposable
{
    public HierarchyWorkspace Workspace { get; set; }
    public ObservableCollection<WorkspaceTask> Due { get; } = new();
    public ObservableCollection<WorkspaceTask> Overdue { get; } = new();
    public ObservableCollection<TimeBlock> Scheduled { get; } = new();
    public ObservableCollection<ReminderRecord> Reminders { get; } = new();
    private string error = "";
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public void WatchSession() => session.Changed += SessionChanged;
    private void SessionChanged()
    {
        if (Workspace != HierarchyWorkspace.SyncedAccount) return;
        Due.Clear(); Overdue.Clear(); Scheduled.Clear(); Reminders.Clear(); Error = "Account changed. Refresh to load the active account's plan.";
    }
    public void Dispose() => session.Changed -= SessionChanged;
    public async Task RefreshAsync()
    {
        Due.Clear(); Overdue.Clear(); Scheduled.Clear(); Reminders.Clear(); Error = "";
        var workspace = Workspace; var version = session.Version;
        try
        {
            var clock = time ?? TimeProvider.System;
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var rows = await reader.ReadAsync(workspace, version);
            var scope = reader.Scope(workspace, version);
            var blocks = await calendar.ReadAsync(scope);
            var alerts = await reminders.ReadAsync();
            _ = reader.Scope(workspace, version);
            if (workspace != Workspace) return;
            foreach (var row in rows.Where(r => !r.Data.IsCompleted && r.Data.DueDate == today)) Due.Add(row);
            foreach (var row in rows.Where(r => !r.Data.IsCompleted && r.Data.DueDate < today)) Overdue.Add(row);
            var start = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), clock.GetLocalNow().Offset);
            foreach (var block in blocks.Where(b => b.StartUtc < start.AddDays(1) && b.EndUtc > start).OrderBy(b => b.StartUtc)) Scheduled.Add(block);
            foreach (var alert in alerts.Where(r => r.Scope == scope && DateOnly.FromDateTime(r.FireAtUtc.ToLocalTime().DateTime) == today)) Reminders.Add(alert);
        }
        catch (ApiClientException e) { Error = e.Message; }
        catch (Exception) { Error = "Couldn't load today's saved plan. Your tasks are unchanged; try refreshing."; }
    }
}
