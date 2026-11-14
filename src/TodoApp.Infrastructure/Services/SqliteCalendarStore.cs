using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Calendar;

namespace TodoApp.Infrastructure.Services;

public sealed class CalendarConflictException() : InvalidOperationException("This block changed or was removed in another window. Refresh and review before trying again.");

// Independent device-local planning file. No task/outbox mutation or provider tokens.
public sealed partial class SqliteCalendarStore(string path, TimeProvider? clock = null)
{
    public Task<IReadOnlyList<TimeBlock>> ReadAsync(string scope) => ExecuteAsync(scope, Read);

    public Task<TimeBlock> SaveAsync(string scope, Guid id, TimeBlockDraft draft, long? expectedRevision) => ExecuteAsync(scope, (connection, transaction, partition) =>
    {
        var rows = Read(connection, transaction, partition);
        var existing = rows.SingleOrDefault(b => b.Id == id);
        if (expectedRevision is null ? existing is not null : existing?.Revision != expectedRevision) throw new CalendarConflictException();
        if (existing is not null && (draft.TaskId != existing.TaskId || draft.Title != existing.Title))
            throw new ArgumentException("A saved block keeps its captured task/title. Create a new block to plan another task.");
        if (existing is null && rows.Count >= 500) throw new ArgumentException("This workspace has 500 blocks. Remove older blocks before adding another.");
        var block = TimeBlockRules.Create(id, scope, draft, checked((expectedRevision ?? 0) + 1), (clock ?? TimeProvider.System).GetUtcNow());
        if (rows.Any(other => TimeBlockRules.Overlaps(block, other))) throw new ArgumentException("This block overlaps another saved block in this workspace. Choose a free time.");
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = existing is null
            ? "INSERT INTO CalendarBlocks (Id,Scope,Revision,Data) VALUES ($id,$scope,$revision,$data);"
            : "UPDATE CalendarBlocks SET Revision=$revision,Data=$data WHERE Scope=$scope AND Id=$id;";
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$id", id.ToString("N"));
        command.Parameters.AddWithValue("$revision", block.Revision); command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(block));
        command.ExecuteNonQuery(); return block;
    });

    public Task DeleteAsync(string scope, Guid id, long expectedRevision) => ExecuteAsync(scope, (connection, transaction, _) =>
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM CalendarBlocks WHERE Scope=$scope AND Id=$id AND Revision=$revision;";
        command.Parameters.AddWithValue("$scope", scope); command.Parameters.AddWithValue("$id", id.ToString("N")); command.Parameters.AddWithValue("$revision", expectedRevision);
        if (command.ExecuteNonQuery() != 1) throw new CalendarConflictException();
        return true;
    });

    private static IReadOnlyList<TimeBlock> Read(SqliteConnection connection, SqliteTransaction transaction, string scope)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Id,Revision,Data FROM CalendarBlocks WHERE Scope=$scope;"; command.Parameters.AddWithValue("$scope", scope);
        using var reader = command.ExecuteReader(); var result = new List<TimeBlock>();
        while (reader.Read())
        {
            var block = JsonSerializer.Deserialize<TimeBlock>(reader.GetString(2)) ?? throw new InvalidDataException("Unreadable calendar block. Nothing was reset.");
            if (block.Scope != scope || block.Id == Guid.Empty || block.Id.ToString("N") != reader.GetString(0) || block.Revision != reader.GetInt64(1)
                || block.TaskId == Guid.Empty || string.IsNullOrWhiteSpace(block.Title) || block.Title.Length > 200 || block.Title.Any(char.IsControl)
                || block.Revision is < 1 or > int.MaxValue || block.StartUtc.Offset != TimeSpan.Zero || block.EndUtc.Offset != TimeSpan.Zero
                || block.StartUtc >= block.EndUtc || (block.EndUtc - block.StartUtc).TotalMinutes is < 1 or > 1440
                || block.WallStart.Kind != DateTimeKind.Unspecified || block.WallStart.Year is < 2000 or > 2099
                || block.WallStart.Ticks % TimeSpan.TicksPerMinute != 0 || (block.EndUtc - block.StartUtc).Ticks % TimeSpan.TicksPerMinute != 0
                || block.UpdatedUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(block.TimeZoneId))
                throw new InvalidDataException("Invalid calendar block. Nothing was reset.");
            // UTC remains authoritative even if OS time-zone rules change later.
            result.Add(block);
        }
        return result.OrderBy(b => b.StartUtc).ThenBy(b => b.Id).ToArray();
    }

    private Task<T> ExecuteAsync<T>(string scope, Func<SqliteConnection, SqliteTransaction, string, T> action) => Task.Run(() =>
    {
        TimeBlockRules.ValidateScope(scope);
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = full, Pooling = false, DefaultTimeout = 5 }.ToString()); connection.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var schema = connection.CreateCommand(); schema.Transaction = transaction; schema.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(schema.ExecuteScalar());
        if (version is < 0 or > 3) throw new InvalidDataException("Calendar data requires a compatible app. Nothing was reset.");
        if (version == 0)
        {
            schema.CommandText = """
                CREATE TABLE CalendarBlocks (Id TEXT PRIMARY KEY, Scope TEXT NOT NULL, Revision INTEGER NOT NULL CHECK(Revision>0), Data TEXT NOT NULL);
                CREATE INDEX IX_CalendarBlocks_Scope ON CalendarBlocks(Scope);
                PRAGMA user_version=1;
                """;
            schema.ExecuteNonQuery();
        }
        if (version < 2)
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS GoogleEventLinks (
                    Scope TEXT NOT NULL, BlockId TEXT NOT NULL, Revision INTEGER NOT NULL CHECK(Revision>0), Data TEXT NOT NULL,
                    PRIMARY KEY (Scope,BlockId));
                PRAGMA user_version=2;
                """;
            schema.ExecuteNonQuery();
        }
        if (version < 3)
        {
            // Additive JSON review fields and Detached state require the newer reader.
            schema.CommandText = "PRAGMA user_version=3;"; schema.ExecuteNonQuery();
        }
        var result = action(connection, transaction, scope); transaction.Commit(); return result;
    });
}
