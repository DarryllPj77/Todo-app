using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Focus;

namespace TodoApp.Infrastructure.Services;

public sealed class FocusConflictException() : InvalidOperationException("Focus history changed in another window. Refresh before retrying.");

// Independent device-local ledger; no task/sync rows or bearer tokens are written.
public sealed class SqliteFocusStore(string path)
{
    public IDisposable? TryAcquireRuntimeLease()
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        try { return new FileStream(full + ".runtime.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }
    public Task<IReadOnlyList<FocusEntry>> ReadAsync(string scope) => ExecuteAsync((connection, tx) =>
    {
        ValidateScope(scope); using var command = Command(connection, tx, "SELECT Sequence,Revision,Active,State,Id,Scope FROM FocusSessions WHERE Scope=$scope ORDER BY Sequence DESC;");
        command.Parameters.AddWithValue("$scope", scope); var rows = new List<FocusEntry>(); using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(Read(reader, scope)); return (IReadOnlyList<FocusEntry>)rows.AsReadOnly();
    });
    public Task<FocusEntry?> ActiveAsync() => ExecuteAsync((connection, tx) =>
    {
        using var command = Command(connection, tx, "SELECT Sequence,Revision,Active,State,Id,Scope FROM FocusSessions WHERE Active=1;");
        using var reader = command.ExecuteReader(); return reader.Read() ? Read(reader) : null;
    });
    public Task<FocusPreferences> PreferencesAsync(string scope) => ExecuteAsync((connection, tx) =>
    {
        ValidateScope(scope); using var command = Command(connection, tx, "SELECT Settings FROM FocusPreferences WHERE Scope=$scope;"); command.Parameters.AddWithValue("$scope", scope);
        return command.ExecuteScalar() is string json ? (JsonSerializer.Deserialize<FocusPreferences>(json) ?? throw new InvalidDataException("Unreadable focus settings.")).Validate() : new();
    });
    public Task<FocusEntry> StartAsync(FocusSession session, FocusPreferences preferences) => ExecuteAsync((connection, tx) =>
    {
        FocusRules.Validate(session); ValidateScope(session.Scope); preferences.Validate();
        if (session.Status != FocusStatus.Running || session.ElapsedTicks != 0) throw new ArgumentException("A new focus session must start from zero.");
        using (var check = Command(connection, tx, "SELECT count(*) FROM FocusSessions WHERE Active=1;"))
            if ((long)check.ExecuteScalar()! != 0) throw new InvalidOperationException("A session is already running or paused. Finish it before starting another.");
        using (var settings = Command(connection, tx, "INSERT INTO FocusPreferences(Scope,Settings) VALUES($scope,$settings) ON CONFLICT(Scope) DO UPDATE SET Settings=$settings;"))
        { settings.Parameters.AddWithValue("$scope", session.Scope); settings.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(preferences)); settings.ExecuteNonQuery(); }
        using var insert = Command(connection, tx, "INSERT INTO FocusSessions(Id,Scope,Revision,Active,State) VALUES($id,$scope,1,1,$state) RETURNING Sequence;");
        insert.Parameters.AddWithValue("$id", session.Id.ToString("N")); insert.Parameters.AddWithValue("$scope", session.Scope); insert.Parameters.AddWithValue("$state", JsonSerializer.Serialize(session));
        return new FocusEntry((long)insert.ExecuteScalar()!, 1, session);
    });
    public Task<FocusEntry> SaveAsync(FocusEntry expected, FocusSession next) => ExecuteAsync((connection, tx) =>
    {
        FocusRules.Validate(next); ValidateScope(next.Scope);
        FocusEntry old;
        using (var check = Command(connection, tx, "SELECT Sequence,Revision,Active,State,Id,Scope FROM FocusSessions WHERE Id=$id AND Scope=$scope;"))
        {
            check.Parameters.AddWithValue("$id", expected.Session.Id.ToString("N")); check.Parameters.AddWithValue("$scope", expected.Session.Scope);
            using var reader = check.ExecuteReader(); if (!reader.Read()) throw new FocusConflictException(); old = Read(reader, expected.Session.Scope);
        }
        if (old != expected) throw new FocusConflictException();
        if (!old.Session.IsActive) return old.Session == next ? old : throw new FocusConflictException();
        if (next.Id != old.Session.Id || next.Scope != old.Session.Scope || next.TaskId != old.Session.TaskId || next.Title != old.Session.Title ||
            next.TaskRecurrence != old.Session.TaskRecurrence || next.Kind != old.Session.Kind || next.TargetTicks != old.Session.TargetTicks ||
            next.StartedAtUtc != old.Session.StartedAtUtc || next.UpdatedAtUtc < old.Session.UpdatedAtUtc || next.ElapsedTicks < old.Session.ElapsedTicks ||
            (old.Session.Status == FocusStatus.Paused && next.ElapsedTicks != old.Session.ElapsedTicks))
            throw new ArgumentException("Saved focus identity and credited time cannot be rewritten.");
        var revision = checked(old.Revision + 1);
        using var write = Command(connection, tx, "UPDATE FocusSessions SET Revision=$revision,Active=$active,State=$state WHERE Id=$id AND Revision=$expected;");
        write.Parameters.AddWithValue("$revision", revision); write.Parameters.AddWithValue("$active", next.IsActive ? 1 : 0); write.Parameters.AddWithValue("$state", JsonSerializer.Serialize(next));
        write.Parameters.AddWithValue("$id", next.Id.ToString("N")); write.Parameters.AddWithValue("$expected", old.Revision);
        if (write.ExecuteNonQuery() != 1) throw new FocusConflictException(); return new FocusEntry(old.Sequence, revision, next);
    });
    private static void ValidateScope(string scope)
    { if (string.IsNullOrWhiteSpace(scope) || scope.Length > 2048) throw new ArgumentException("Select a valid focus workspace."); }
    private static FocusEntry Read(SqliteDataReader reader, string? scope = null)
    {
        var session = JsonSerializer.Deserialize<FocusSession>(reader.GetString(3)) ?? throw new InvalidDataException("Unreadable focus history. Nothing was reset.");
        FocusRules.Validate(session);
        if (session.Id.ToString("N") != reader.GetString(4) || session.Scope != reader.GetString(5) ||
            (scope is not null && session.Scope != scope) || (reader.GetInt32(2) == 1) != session.IsActive || reader.GetInt64(1) < 1)
            throw new InvalidDataException("Invalid focus ledger identity. Nothing was reset.");
        return new(reader.GetInt64(0), reader.GetInt64(1), session);
    }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction tx, string sql)
    { var command = connection.CreateCommand(); command.Transaction = tx; command.CommandText = sql; return command; }
    private Task<T> ExecuteAsync<T>(Func<SqliteConnection, SqliteTransaction, T> action) => Task.Run(() =>
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = full, Pooling = false, DefaultTimeout = 5 }.ToString()); connection.Open();
        using var tx = connection.BeginTransaction();
        using (var schema = Command(connection, tx, "PRAGMA user_version;"))
        {
            var version = Convert.ToInt32(schema.ExecuteScalar());
            if (version > 1) throw new InvalidDataException("Focus history requires a newer app. Nothing was reset.");
            if (version == 0)
            {
                schema.CommandText = """
                    CREATE TABLE FocusSessions(Sequence INTEGER PRIMARY KEY AUTOINCREMENT,Id TEXT NOT NULL UNIQUE,Scope TEXT NOT NULL,
                        Revision INTEGER NOT NULL CHECK(Revision>0),Active INTEGER NOT NULL CHECK(Active IN(0,1)),State TEXT NOT NULL);
                    CREATE UNIQUE INDEX IX_FocusSessions_OneActive ON FocusSessions(Active) WHERE Active=1;
                    CREATE INDEX IX_FocusSessions_Scope ON FocusSessions(Scope,Sequence);
                    CREATE TABLE FocusPreferences(Scope TEXT PRIMARY KEY,Settings TEXT NOT NULL);
                    PRAGMA user_version=1;
                    """;
                schema.ExecuteNonQuery();
            }
        }
        var result = action(connection, tx); tx.Commit(); return result;
    });
}
