using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;

namespace TodoApp.Domain.Rules;

public static class TaskQueries
{
    public static IReadOnlyList<TaskItem> ApplyFilter(IEnumerable<TaskItem> tasks, TaskFilter filter, DateOnly today)
    {
        var filteredTasks = filter switch
        {
            TaskFilter.All => tasks,
            TaskFilter.Pending => tasks.Where(task => !task.IsCompleted),
            TaskFilter.Completed => tasks.Where(task => task.IsCompleted),
            TaskFilter.DueToday => tasks.Where(task => task.DueDate == today),
            TaskFilter.HighPriority => tasks.Where(task => task.Priority == TaskPriority.High),
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Choose a defined task filter.")
        };

        return filteredTasks.OrderBy(task => task.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(task => task.Id).ToList().AsReadOnly();
    }
}
