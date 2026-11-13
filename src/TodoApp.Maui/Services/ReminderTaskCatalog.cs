using TodoApp.Domain.Reminders;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

public sealed class ReminderTaskCatalog(SqliteLocalTaskService deviceTasks, SqliteSyncStore sync, ClientSession session)
{
    public string? ActiveScope => session.Current is { } saved ? SqliteSyncStore.Partition(saved.Origin, saved.User.Id) : null;
    public async Task<ReminderTask[]> ReadAsync()
    {
        var device = await deviceTasks.GetTasksAsync();
        var result = device.Select(t => new ReminderTask("device", t.Id, t.Title, t.DueDate, t.IsCompleted)).ToList();
        if (ActiveScope is { } scope)
        {
            var state = await sync.ReadAsync(scope);
            result.AddRange(state.Rows.Values.Where(r => r.Local is not null).Select(r =>
                new ReminderTask(scope, r.Id, r.Local!.Title, r.Local.DueDate, r.Local.IsCompleted)));
        }
        return result.ToArray();
    }
}
