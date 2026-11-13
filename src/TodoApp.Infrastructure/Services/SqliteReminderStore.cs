using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Reminders;

namespace TodoApp.Infrastructure.Services;

public sealed class ReminderState
{
    public int NextId { get; set; } = 1;
    public Dictionary<string, ReminderRecord> Rows { get; set; } = new();
}

// Independent of the device task schema and server/sync schemas. Native IDs are
// allocated transactionally and never reused, including after cancellation.
public sealed class SqliteReminderStore(string path)
{
    public Task<ReminderRecord[]> ReadAsync() => ExecuteAsync(s => s.Rows.Values.OrderBy(r => r.FireAtUtc).ToArray(), false);
    public Task PutAsync(ReminderRecord row) => ExecuteAsync(state =>
    {
        if (row.TaskId == Guid.Empty || string.IsNullOrWhiteSpace(row.Scope)) throw new ArgumentException("Select a task.");
        if (state.Rows.TryGetValue(row.Key, out var existing)) row.Id = existing.Id;
        else
        {
            row.Id = state.NextId;
            state.NextId = checked(state.NextId + 1);
        }
        state.Rows[row.Key] = row;
        return true;
    }, true);

    private Task<T> ExecuteAsync<T>(Func<ReminderState, T> action, bool write) => Task.Run(() =>
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = full, Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(command.ExecuteScalar()) > 1) throw new InvalidDataException("Reminder data requires a newer app. Nothing was reset.");
        command.CommandText = "CREATE TABLE IF NOT EXISTS ReminderSettings (Id INTEGER PRIMARY KEY CHECK(Id=1), State TEXT NOT NULL); PRAGMA user_version=1;";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT State FROM ReminderSettings WHERE Id=1;";
        var json = command.ExecuteScalar() as string;
        var state = json is null ? new ReminderState() : JsonSerializer.Deserialize<ReminderState>(json)
            ?? throw new InvalidDataException("Unreadable reminder data. Nothing was reset.");
        var result = action(state);
        if (write)
        {
            command.CommandText = "INSERT INTO ReminderSettings (Id,State) VALUES (1,$state) ON CONFLICT(Id) DO UPDATE SET State=$state;";
            command.Parameters.AddWithValue("$state", JsonSerializer.Serialize(state)); command.ExecuteNonQuery();
        }
        transaction.Commit(); return result;
    });
}
