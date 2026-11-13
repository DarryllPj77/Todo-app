using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Views;

namespace TodoApp.Infrastructure.Services;

public sealed class SavedViewConflictException : InvalidOperationException
{
    public SavedViewConflictException() : base("This saved view changed or was deleted in another window. Refresh and review before saving/deleting it.") { }
}

// Definitions are local preferences; no task rows, server owners, or outbox
// operations are changed. The independent file has schema version 3.
public sealed class SqliteTaskViewStore(string path)
{
    public Task<IReadOnlyList<SavedTaskView>> ReadAsync(string scope) => ExecuteAsync(scope, (connection, transaction) =>
    {
        using var command = Command(connection, transaction, "SELECT Id,Name,Revision,Definition FROM SavedTaskViews WHERE Scope=$scope ORDER BY NameKey,Id;", scope);
        using var reader = command.ExecuteReader(); var rows = new List<SavedTaskView>();
        while (reader.Read()) rows.Add(new(Guid.Parse(reader.GetString(0)), scope, reader.GetString(1), reader.GetInt64(2),
            (JsonSerializer.Deserialize<TaskViewDefinition>(reader.GetString(3)) ?? throw new InvalidDataException("Unreadable saved view.")).Normalize()));
        return (IReadOnlyList<SavedTaskView>)rows.AsReadOnly();
    });
    public Task<SavedTaskView> CreateAsync(string scope, string name, TaskViewDefinition definition) =>
        SaveAsync(scope, Guid.NewGuid(), null, name, definition);
    public Task<SavedTaskView> UpdateAsync(string scope, Guid id, long expectedRevision, string name, TaskViewDefinition definition) =>
        expectedRevision <= 0 ? throw new ArgumentException("Refresh the saved view before updating it.") : SaveAsync(scope, id, expectedRevision, name, definition);
    private Task<SavedTaskView> SaveAsync(string scope, Guid id, long? revision, string inputName, TaskViewDefinition definition)
    {
        var name = (inputName ?? "").Trim();
        if (name.Length is < 1 or > 60) throw new ArgumentException("Use a saved-view name of 1-60 characters.");
        if (id == Guid.Empty) throw new ArgumentException("Choose a saved view.");
        var normalized = definition.Normalize();
        return ExecuteAsync(scope, (connection, transaction) =>
        {
            if (revision is { } expected)
            {
                using var check = Command(connection, transaction, "SELECT Revision FROM SavedTaskViews WHERE Scope=$scope AND Id=$id;", scope);
                check.Parameters.AddWithValue("$id", id.ToString("N"));
                if (check.ExecuteScalar() is not long current || current != expected) throw new SavedViewConflictException();
            }
            using (var duplicate = Command(connection, transaction, "SELECT count(*) FROM SavedTaskViews WHERE Scope=$scope AND NameKey=$name AND Id<>$id;", scope))
            {
                duplicate.Parameters.AddWithValue("$name", name.ToUpperInvariant()); duplicate.Parameters.AddWithValue("$id", id.ToString("N"));
                if ((long)duplicate.ExecuteScalar()! > 0) throw new ArgumentException("That saved-view name already exists in this workspace.");
            }
            if (revision is null)
            {
                using var count = Command(connection, transaction, "SELECT count(*) FROM SavedTaskViews WHERE Scope=$scope;", scope);
                if ((long)count.ExecuteScalar()! >= 50) throw new ArgumentException("This workspace already has 50 saved views. Remove one before adding another.");
            }
            var nextRevision = checked((revision ?? 0) + 1);
            using var write = Command(connection, transaction, revision is null
                ? "INSERT INTO SavedTaskViews (Id,Scope,Name,NameKey,Revision,Definition) VALUES ($id,$scope,$name,$key,$revision,$definition);"
                : "UPDATE SavedTaskViews SET Name=$name,NameKey=$key,Revision=$revision,Definition=$definition WHERE Scope=$scope AND Id=$id;", scope);
            write.Parameters.AddWithValue("$id", id.ToString("N")); write.Parameters.AddWithValue("$name", name);
            write.Parameters.AddWithValue("$key", name.ToUpperInvariant()); write.Parameters.AddWithValue("$revision", nextRevision);
            write.Parameters.AddWithValue("$definition", JsonSerializer.Serialize(normalized)); write.ExecuteNonQuery();
            return new SavedTaskView(id, scope, name, nextRevision, normalized);
        });
    }
    public Task DeleteAsync(string scope, Guid id, long expectedRevision) => ExecuteAsync(scope, (connection, transaction) =>
    {
        using var command = Command(connection, transaction, "DELETE FROM SavedTaskViews WHERE Scope=$scope AND Id=$id AND Revision=$revision;", scope);
        command.Parameters.AddWithValue("$id", id.ToString("N")); command.Parameters.AddWithValue("$revision", expectedRevision);
        if (command.ExecuteNonQuery() != 1) throw new SavedViewConflictException();
        return true;
    });
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, string scope)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        command.Parameters.AddWithValue("$scope", scope); return command;
    }
    private Task<T> ExecuteAsync<T>(string scope, Func<SqliteConnection, SqliteTransaction, T> action) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 2048) throw new ArgumentException("Select a valid workspace.");
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = full, Pooling = false, DefaultTimeout = 5 }.ToString()); connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = transaction; schema.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt32(schema.ExecuteScalar()) > 3) throw new InvalidDataException("Saved views require a newer app. Nothing was reset.");
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS SavedTaskViews (
                    Id TEXT PRIMARY KEY, Scope TEXT NOT NULL, Name TEXT NOT NULL, NameKey TEXT NOT NULL,
                    Revision INTEGER NOT NULL CHECK(Revision>0), Definition TEXT NOT NULL,
                    UNIQUE(Scope,NameKey)); PRAGMA user_version=3;
                """;
            schema.ExecuteNonQuery();
        }
        var result = action(connection, transaction); transaction.Commit(); return result;
    });
}
