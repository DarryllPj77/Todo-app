using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Location;

namespace TodoApp.Infrastructure.Services;

public enum BackgroundGeofenceState { Pending, Active, Stopped }
public sealed record BackgroundGeofenceRun(Guid Id, long Revision, LocationReminder Reminder, DateTimeOffset StartedUtc,
    DateTimeOffset ExpiresUtc, bool OutsideObserved, BackgroundGeofenceState State, string Status)
{
    public string EntryId => "todo-background-v1-" + Id.ToString("N") + "-enter";
    public string ExitId => "todo-background-v1-" + Id.ToString("N") + "-exit";
    public override string ToString() => "Protected background geofence run";
}

// Independent encrypted journal. The existing location key MUST already exist;
// this store never creates or replaces an OS-protected key.
public sealed class SqliteBackgroundGeofenceStore(string path, ILocationEncryptionKeyStore keys)
{
    public Task<BackgroundGeofenceRun?> ReadAsync() => !File.Exists(path) ? Task.FromResult<BackgroundGeofenceRun?>(null) : ExecuteAsync(Read);
    public Task<BackgroundGeofenceRun> SaveAsync(BackgroundGeofenceRun? expected, BackgroundGeofenceRun next) => ExecuteAsync((connection, transaction, key) =>
    {
        var current = Read(connection, transaction, key);
        if (current != expected || next.Revision != checked((expected?.Revision ?? 0) + 1)) throw new LocationReminderConflictException();
        Validate(next);
        if ((expected is null || expected.Id != next.Id) && (next.State != BackgroundGeofenceState.Pending || next.OutsideObserved))
            throw new InvalidDataException("A new background run must begin pending without outside evidence.");
        if (expected is not null && expected.Id == next.Id &&
            (next.Reminder != expected.Reminder || next.StartedUtc != expected.StartedUtc || next.ExpiresUtc != expected.ExpiresUtc
             || expected.State == BackgroundGeofenceState.Stopped && next.State != BackgroundGeofenceState.Stopped
             || expected.State == BackgroundGeofenceState.Active && next.State == BackgroundGeofenceState.Pending
             || expected.OutsideObserved && !next.OutsideObserved)) throw new InvalidDataException("Invalid background run transition.");
        if (expected is not null && expected.Id != next.Id && expected.State != BackgroundGeofenceState.Stopped)
            throw new LocationReminderConflictException();
        var plain = JsonSerializer.SerializeToUtf8Bytes(next); var encrypted = new byte[plain.Length];
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
        using (var cipher = new AesGcm(key, 16)) cipher.Encrypt(nonce, plain, encrypted, tag, Aad(next.Revision));
        var data = "v1." + Convert.ToBase64String(nonce) + "." + Convert.ToBase64String(tag) + "." + Convert.ToBase64String(encrypted);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO BackgroundRun(Id,Revision,Data) VALUES(1,$revision,$data) ON CONFLICT(Id) DO UPDATE SET Revision=$revision,Data=$data;";
        command.Parameters.AddWithValue("$revision", next.Revision); command.Parameters.AddWithValue("$data", data); command.ExecuteNonQuery(); return next;
    });
    private static byte[] Aad(long revision) => Encoding.UTF8.GetBytes("Todo.BackgroundGeofence.v1|" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static void Validate(BackgroundGeofenceRun row)
    {
        if (row.Id == Guid.Empty || row.Revision <= 0 || row.Reminder is null || row.Reminder.Scope != "device" || !row.Reminder.Enabled
            || row.Reminder.Id == Guid.Empty || row.Reminder.ArmId == Guid.Empty || row.Reminder.NotificationId >= 0
            || row.Reminder.Recurrence is null || string.IsNullOrWhiteSpace(row.Reminder.TaskTitle) || row.Reminder.TaskTitle.Length > 200
            || !Enum.IsDefined(row.State) || row.StartedUtc.Offset != TimeSpan.Zero || row.ExpiresUtc != row.StartedUtc.AddMinutes(30)
            || string.IsNullOrWhiteSpace(row.Status) || row.Status.Length > 500) throw new InvalidDataException("Invalid protected background run. Nothing was reset.");
        LocationRules.Validate(new(row.Reminder.TaskId, row.Reminder.PlaceName, row.Reminder.Latitude, row.Reminder.Longitude, row.Reminder.RadiusMeters));
        TodoApp.Domain.Recurrence.RecurrenceRules.Validate(row.Reminder.Recurrence);
    }
    private static BackgroundGeofenceRun? Read(SqliteConnection connection, SqliteTransaction transaction, byte[] key)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT Revision,Data FROM BackgroundRun WHERE Id=1;";
        using var reader = command.ExecuteReader(); if (!reader.Read()) return null;
        try
        {
            var revision = reader.GetInt64(0); var parts = reader.GetString(1).Split('.');
            if (parts.Length != 4 || parts[0] != "v1") throw new InvalidDataException();
            var encrypted = Convert.FromBase64String(parts[3]); if (encrypted.Length > 32768) throw new InvalidDataException();
            var plain = new byte[encrypted.Length]; using (var cipher = new AesGcm(key, 16))
                cipher.Decrypt(Convert.FromBase64String(parts[1]), encrypted, Convert.FromBase64String(parts[2]), plain, Aad(revision));
            var row = JsonSerializer.Deserialize<BackgroundGeofenceRun>(plain) ?? throw new InvalidDataException(); Validate(row);
            if (row.Revision != revision) throw new InvalidDataException(); return row;
        }
        catch (Exception error) when (error is InvalidDataException or CryptographicException or JsonException or ArgumentException or FormatException)
        { throw new InvalidDataException("Background location data could not be authenticated/read. Nothing was reset; Stop can still block callbacks and attempt native cleanup."); }
    }
    private Task<T> ExecuteAsync<T>(Func<SqliteConnection, SqliteTransaction, byte[], T> action) => Task.Run(async () =>
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = full, Pooling = false, DefaultTimeout = 5 }.ToString()); await connection.OpenAsync();
        using var transaction = connection.BeginTransaction(deferred: false); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;"; var version = Convert.ToInt32(command.ExecuteScalar());
        if (version is < 0 or > 1) throw new InvalidDataException("Background location journal requires a compatible app. Nothing was reset.");
        if (version == 0)
        {
            command.CommandText = "CREATE TABLE BackgroundRun(Id INTEGER PRIMARY KEY CHECK(Id=1),Revision INTEGER NOT NULL CHECK(Revision>0),Data TEXT NOT NULL); PRAGMA user_version=1;";
            command.ExecuteNonQuery();
        }
        var key = await keys.GetAsync(allowCreate: false);
        if (key.Length != 32) throw new InvalidDataException("Original protected location key is unavailable. Nothing was reset.");
        var result = action(connection, transaction, key); transaction.Commit(); return result;
    });
}

public sealed class GeofenceProcessLease(string path)
{
    public async Task<IDisposable> AcquireAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 49) { await Task.Delay(100, token); }
            catch (IOException) { throw new InvalidOperationException("Another window/process owns native location work. Stop its foreground session or retry background cleanup shortly."); }
        }
    }
}

// Nonsecret, key-independent opt-out generation. A pending Start cannot undo
// a later Stop: enabling is a transactional compare-and-swap on Generation.
public sealed class SqliteBackgroundGeofenceControl(string path)
{
    public (bool Blocked, long Generation) Read() => !File.Exists(path) ? (true, 0L) : Execute(command =>
    {
        command.CommandText = "SELECT Blocked,Generation FROM BackgroundControl WHERE Id=1;";
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new InvalidDataException("Background opt-out metadata is missing. Nothing was reset.");
        return (reader.GetInt32(0) != 0, reader.GetInt64(1));
    });
    public long Block() => Execute(command =>
    {
        command.CommandText = "UPDATE BackgroundControl SET Blocked=1,Generation=Generation+1 WHERE Id=1 AND Generation<9223372036854775807 RETURNING Generation;";
        return command.ExecuteScalar() is long generation ? generation : throw new InvalidDataException("Background opt-out metadata is invalid/exhausted.");
    });
    public void Enable(long expectedGeneration) => Execute(command =>
    {
        command.CommandText = "UPDATE BackgroundControl SET Blocked=0 WHERE Id=1 AND Blocked=1 AND Generation=$generation;";
        command.Parameters.AddWithValue("$generation", expectedGeneration);
        if (command.ExecuteNonQuery() != 1) throw new OperationCanceledException("A newer Stop/start superseded this background opt-in.");
        return true;
    });
    private T Execute<T>(Func<SqliteCommand, T> action)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 5 }.ToString()); connection.Open();
        using var transaction = connection.BeginTransaction(deferred: false); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;"; var version = Convert.ToInt32(command.ExecuteScalar());
        if (version is < 0 or > 1) throw new InvalidDataException("Background opt-out schema is incompatible. Nothing was reset.");
        if (version == 0)
        {
            command.CommandText = "CREATE TABLE BackgroundControl(Id INTEGER PRIMARY KEY CHECK(Id=1),Blocked INTEGER NOT NULL CHECK(Blocked IN (0,1)),Generation INTEGER NOT NULL CHECK(Generation>=0)); INSERT INTO BackgroundControl VALUES(1,1,0); PRAGMA user_version=1;";
            command.ExecuteNonQuery();
        }
        var result = action(command); transaction.Commit(); return result;
    }
}
