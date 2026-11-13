using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Rules;
using TodoApp.Domain.Relationships;
using TodoApp.Domain.Services;
using TodoApp.Infrastructure.Persistence;

namespace TodoApp.Infrastructure.Services;

public sealed class SqliteLocalTaskService : ILocalTaskService
{
    private readonly SqliteTaskDatabase database;
    private readonly TimeProvider timeProvider;

    public SqliteLocalTaskService(string databasePath, TimeProvider? timeProvider = null)
    {
        database = new SqliteTaskDatabase(databasePath);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(TaskFilter filter = TaskFilter.All,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync((connection, transaction, token) =>
        {
            var tasks = LoadTasks(connection, transaction, token);
            var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            return TaskQueries.ApplyFilter(tasks.Values, filter, today);
        }, cancellationToken);
    }

    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync((connection, transaction, token) =>
        {
            var tasks = LoadTasks(connection, transaction, token);
            tasks.TryGetValue(id, out var task);
            return task;
        }, cancellationToken);
    }

    public Task<TaskItem> CreateAsync(TaskDetails details, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync((connection, transaction, token) =>
        {
            ArgumentNullException.ThrowIfNull(details);
            var id = Guid.NewGuid();
            var existing = LoadTasks(connection, transaction, token);
            TaskDependencies.ValidateChange(Nodes(existing).Append(new(id, TaskPrerequisites.Empty, false)), id, details.Prerequisites ?? TaskPrerequisites.Empty);
            var links = existing.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)).Append(new TaskLink(id, null));
            TaskHierarchy.ValidateMove(links, id, details.Parent?.Id);
            var category = ResolveCategory(connection, transaction, details.Category);
            var task = TaskItem.CreateWithId(id, details.Title, details.Description, details.DueDate, details.Priority, category, details.Tags);
            task.SetParent(details.Parent ?? new TaskParent(null));
            task.SetPrerequisites(details.Prerequisites ?? TaskPrerequisites.Empty);
            SaveCategory(connection, transaction, category);
            SaveTask(connection, transaction, task, insert: true);
            return task;
        }, cancellationToken);
    }

    public Task<bool> UpdateAsync(Guid id, TaskDetails details, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync((connection, transaction, token) =>
        {
            ArgumentNullException.ThrowIfNull(details);
            var tasks = LoadTasks(connection, transaction, token);
            if (!tasks.TryGetValue(id, out var task))
            {
                return false;
            }

            if (details.Prerequisites is { } prerequisites)
            {
                TaskDependencies.ValidateChange(Nodes(tasks), id, prerequisites, task.IsCompleted && !task.Prerequisites.Equals(prerequisites));
                task.SetPrerequisites(prerequisites);
            }
            if (details.Parent is { } parent)
            {
                TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id, parent.Id);
                task.SetParent(parent);
            }
            task.UpdateDetails(details.Title, details.Description, details.DueDate, details.Priority, details.Tags);
            var category = task.CategoryId == details.Category?.Id
                ? task.Category
                : ResolveCategory(connection, transaction, details.Category);
            task.SetCategory(category);
            SaveCategory(connection, transaction, category);
            SaveTask(connection, transaction, task, insert: false);
            return true;
        }, cancellationToken);
    }

    public Task<bool> SetCompletedAsync(Guid id, bool isCompleted, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync((connection, transaction, token) =>
        {
            var tasks = LoadTasks(connection, transaction, token);
            if (!tasks.TryGetValue(id, out var task))
            {
                return false;
            }

            if (isCompleted && !task.IsCompleted) TaskDependencies.ValidateChange(Nodes(tasks), id, task.Prerequisites, true);
            task.SetCompleted(isCompleted);
            SaveTask(connection, transaction, task, insert: false);
            return true;
        }, cancellationToken);
    }

    public Task<bool> SetParentAsync(Guid id, Guid? parentId, CancellationToken cancellationToken = default) =>
        ExecuteAsync((connection, transaction, token) =>
        {
            var tasks = LoadTasks(connection, transaction, token);
            if (!tasks.TryGetValue(id, out var task)) return false;
            TaskHierarchy.ValidateMove(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id, parentId);
            task.SetParent(new TaskParent(parentId)); SaveTask(connection, transaction, task, insert: false); return true;
        }, cancellationToken);

    private static IEnumerable<DependencyNode> Nodes(Dictionary<Guid, TaskItem> tasks) => tasks.Values.Select(t => new DependencyNode(t.Id, t.Prerequisites, t.IsCompleted));

    public Task<bool> SetPrerequisitesAsync(Guid id, TaskPrerequisites prerequisites, CancellationToken cancellationToken = default) =>
        ExecuteAsync((connection, transaction, token) =>
        {
            var tasks = LoadTasks(connection, transaction, token);
            if (!tasks.TryGetValue(id, out var task)) return false;
            TaskDependencies.ValidateChange(Nodes(tasks), id, prerequisites, task.IsCompleted && !task.Prerequisites.Equals(prerequisites));
            task.SetPrerequisites(prerequisites); SaveTask(connection, transaction, task, insert: false); return true;
        }, cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync((connection, transaction, token) =>
        {
            var tasks = LoadTasks(connection, transaction, token);
            TaskDependencies.ValidateDelete(Nodes(tasks), id);
            TaskHierarchy.ValidateDelete(tasks.Values.Select(t => new TaskLink(t.Id, t.ParentTaskId)), id);
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM TaskItems WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            return command.ExecuteNonQuery() > 0;
        }, cancellationToken);
    }

    // Microsoft.Data.Sqlite performs synchronous I/O. Run short operations off
    // the UI thread, using a separate connection and transaction for each call.
    private Task<T> ExecuteAsync<T>(Func<SqliteConnection, SqliteTransaction, CancellationToken, T> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = database.OpenConnection(cancellationToken);
            using var transaction = connection.BeginTransaction();
            var result = operation(connection, transaction, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return result;
        }, cancellationToken);
    }

    private static Dictionary<Guid, TaskItem> LoadTasks(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var categories = new Dictionary<Guid, Category>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Name FROM Categories;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var category = Category.Restore(Guid.Parse(reader.GetString(0)), reader.GetString(1));
                categories.Add(category.Id, category);
            }
        }

        var tasks = new Dictionary<Guid, TaskItem>();
        command.CommandText = """
            SELECT Id, Title, Description, IsCompleted, DueDate, Priority, CategoryId, CreatedAt, UpdatedAt, Tags, ParentTaskId, Prerequisites
            FROM TaskItems;
            """;
        using var taskReader = command.ExecuteReader();
        while (taskReader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var category = taskReader.IsDBNull(6) ? null : categories[Guid.Parse(taskReader.GetString(6))];
            var task = TaskItem.Restore(Guid.Parse(taskReader.GetString(0)), taskReader.GetString(1),
                taskReader.IsDBNull(2) ? null : taskReader.GetString(2),
                taskReader.IsDBNull(4) ? null : DateOnly.ParseExact(taskReader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                (TaskPriority)taskReader.GetInt32(5), category, taskReader.GetBoolean(3),
                DateTimeOffset.ParseExact(taskReader.GetString(7), "O", CultureInfo.InvariantCulture),
                DateTimeOffset.ParseExact(taskReader.GetString(8), "O", CultureInfo.InvariantCulture),
                JsonSerializer.Deserialize<TagSet>(taskReader.GetString(9)) ?? throw new InvalidDataException("Unreadable task tags."),
                taskReader.IsDBNull(10) ? null : Guid.Parse(taskReader.GetString(10)),
                JsonSerializer.Deserialize<TaskPrerequisites>(taskReader.GetString(11)) ?? throw new InvalidDataException("Unreadable prerequisites."));
            tasks.Add(task.Id, task);
        }

        return tasks;
    }

    private static Category? ResolveCategory(SqliteConnection connection, SqliteTransaction transaction, Category? input)
    {
        if (input is null)
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Name FROM Categories WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", input.Id.ToString());
        // Reusing a category ID never overwrites its name with a stale caller snapshot.
        var name = command.ExecuteScalar() as string ?? input.Name;
        return Category.Restore(input.Id, name);
    }

    private static void SaveCategory(SqliteConnection connection, SqliteTransaction transaction, Category? category)
    {
        if (category is null)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Categories (Id, Name) VALUES ($id, $name) ON CONFLICT(Id) DO NOTHING;";
        command.Parameters.AddWithValue("$id", category.Id.ToString());
        command.Parameters.AddWithValue("$name", category.Name);
        command.ExecuteNonQuery();
    }

    private static void SaveTask(SqliteConnection connection, SqliteTransaction transaction, TaskItem task, bool insert)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insert
            ? """
                INSERT INTO TaskItems (Id, Title, Description, IsCompleted, DueDate, Priority, CategoryId, CreatedAt, UpdatedAt, Tags, ParentTaskId, Prerequisites)
                VALUES ($id, $title, $description, $completed, $due, $priority, $category, $created, $updated, $tags, $parent, $prerequisites);
                """
            : """
                UPDATE TaskItems SET Title = $title, Description = $description, IsCompleted = $completed,
                    DueDate = $due, Priority = $priority, CategoryId = $category, CreatedAt = $created, UpdatedAt = $updated, Tags = $tags, ParentTaskId = $parent, Prerequisites = $prerequisites
                WHERE Id = $id;
                """;
        command.Parameters.AddWithValue("$id", task.Id.ToString());
        command.Parameters.AddWithValue("$parent", (object?)task.ParentTaskId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$prerequisites", JsonSerializer.Serialize(task.Prerequisites));
        command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(task.Tags));
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$description", (object?)task.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$completed", task.IsCompleted);
        command.Parameters.AddWithValue("$due", (object?)task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$priority", (int)task.Priority);
        command.Parameters.AddWithValue("$category", (object?)task.CategoryId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}
