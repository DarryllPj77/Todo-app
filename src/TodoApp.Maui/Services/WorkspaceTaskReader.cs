using TodoApp.Domain.Services;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.ViewModels;

namespace TodoApp.Maui.Services;

// Presentation projection only. No network, migrations, automatic merges or stored workspace choice.
public sealed class WorkspaceTaskReader(ILocalTaskService device, SqliteSyncStore sync, ClientSession session)
{
    public string Scope(HierarchyWorkspace workspace, int version)
    {
        if (workspace == HierarchyWorkspace.Device) return "device";
        if (workspace != HierarchyWorkspace.SyncedAccount) throw new ArgumentException("Choose a workspace.");
        session.EnsureVersion(version);
        var saved = session.Current ?? throw new ApiClientException("Sign in in Settings > Account, then sync your account tasks.");
        return SqliteSyncStore.Partition(saved.Origin, saved.User.Id);
    }
    public async Task<IReadOnlyList<WorkspaceTask>> ReadAsync(HierarchyWorkspace workspace, int version)
    {
        var scope = Scope(workspace, version);
        IReadOnlyList<WorkspaceTask> rows;
        if (workspace == HierarchyWorkspace.Device)
            rows = (await device.GetTasksAsync()).Select(t => new WorkspaceTask(t.Id,
                new SyncTaskData(t.Title, t.Description, t.DueDate, t.Priority, t.Category?.Name, t.IsCompleted,
                    t.Tags, new(t.ParentTaskId), t.Prerequisites, t.Quadrant, t.Recurrence))).ToArray();
        else rows = (await sync.ReadAsync(scope)).Rows.Values.Where(r => r.Local is not null)
            .Select(r => new WorkspaceTask(r.Id, r.Local!, r.Conflict)).ToArray();
        _ = Scope(workspace, version);
        return rows;
    }
}
