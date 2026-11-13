using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Maui.Services;

// A native notification failure cannot turn a committed SQLite task write into
// a failed task save. ReminderService exposes its own retryable status instead.
public sealed class ReminderAwareTaskService(SqliteLocalTaskService tasks, ReminderService reminders) : ILocalTaskService
{
    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter = TaskFilter.All, CancellationToken token = default) => tasks.GetTasksAsync(filter, token);
    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken token = default) => tasks.GetByIdAsync(id, token);
    public async Task<TaskItem> CreateAsync(TaskDetails details, CancellationToken token = default)
    { var task = await tasks.CreateAsync(details, token); await reminders.RefreshSafelyAsync(); return task; }
    public async Task<bool> UpdateAsync(Guid id, TaskDetails details, CancellationToken token = default)
    { var result = await tasks.UpdateAsync(id, details, token); await reminders.RefreshSafelyAsync(); return result; }
    public async Task<bool> SetCompletedAsync(Guid id, bool completed, CancellationToken token = default)
    { var result = await tasks.SetCompletedAsync(id, completed, token); await reminders.RefreshSafelyAsync(); return result; }
    public async Task<bool> SetParentAsync(Guid id, Guid? parentId, CancellationToken token = default)
    { var result = await tasks.SetParentAsync(id, parentId, token); await reminders.RefreshSafelyAsync(); return result; }
    public async Task<bool> DeleteAsync(Guid id, CancellationToken token = default)
    { var result = await tasks.DeleteAsync(id, token); await reminders.RefreshSafelyAsync(); return result; }
}
