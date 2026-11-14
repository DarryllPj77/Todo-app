using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TodoApp.Domain.Location;
using TodoApp.Domain.Recurrence;

namespace TodoApp.Infrastructure.Services;

public interface ILocationEncryptionKeyStore
{
    Task<byte[]> GetAsync(bool allowCreate);
}
public sealed class LocationReminderConflictException() : InvalidOperationException("This location reminder changed in another window. Refresh and review before trying again.");

// Coordinates/titles are encrypted; AAD binds ciphertext to scope, ID, revision
// and native notification ID. No key reset or plaintext fallback is permitted.
public sealed class SqliteLocationReminderStore(string path, ILocationEncryptionKeyStore keys)
{
    public Task<IReadOnlyList<LocationReminder>> ReadAsync(string scope) => ExecuteAsync(scope, (connection, transaction, key) => Read(connection, transaction, scope, key));
    public Task<LocationReminder> ArmAsync(string scope, Guid id, LocationReminderDraft draft, LocationTask task, long? expectedRevision) => ExecuteAsync(scope, (connection, transaction, key) =>
    {
        LocationRules.Validate(draft); RecurrenceRules.Validate(task.Recurrence);
        if (id == Guid.Empty || task.Id != draft.TaskId || string.IsNullOrWhiteSpace(task.Title) || task.Title.Length > 200)
            throw new ArgumentException("Choose a current pending task.");
        var rows = Read(connection, transaction, scope, key); var prior = rows.SingleOrDefault(r => r.Id == id);
        if (prior?.Revision != expectedRevision) throw new LocationReminderConflictException();
        if (prior is not null && prior.TaskId != task.Id) throw new ArgumentException("Create a new reminder to use another task.");
        var nativeId = prior?.NotificationId;
        if (nativeId is null)
        {
            if (rows.Count >= 100) throw new ArgumentException("This workspace has 100 location reminders. Remove an older one first.");
            using var next = connection.CreateCommand(); next.Transaction = transaction; next.CommandText = "SELECT NextId FROM LocationMetadata WHERE Id=1;";
            nativeId = checked((int)(long)next.ExecuteScalar()!);
            if (nativeId >= 0 || nativeId == int.MinValue) throw new InvalidDataException("Location notification IDs are exhausted/invalid. Nothing was reset.");
            next.CommandText = "UPDATE LocationMetadata SET NextId=NextId-1 WHERE Id=1;"; next.ExecuteNonQuery();
        }
        var row = new LocationReminder(id, scope, checked((prior?.Revision ?? 0) + 1), nativeId.Value, Guid.NewGuid(), task.Id, task.Title, task.Recurrence,
            draft.PlaceName.Trim(), draft.Latitude, draft.Longitude, draft.RadiusMeters, ArrivalState.Armed,
            "Armed. A confirmed outside check followed by an inside check is required; no background monitoring.");
        Write(connection, transaction, row, key); return row;
    });
    public Task<LocationReminder> ChangeAsync(LocationReminder expected, ArrivalState state, string status) => ExecuteAsync(expected.Scope, (connection, transaction, key) =>
    {
        var current = Read(connection, transaction, expected.Scope, key).SingleOrDefault(r => r.Id == expected.Id);
        if (current != expected) throw new LocationReminderConflictException();
        if (!Enum.IsDefined(state) || status.Length > 500 || state is ArrivalState.Armed) throw new ArgumentException("Invalid location reminder transition.");
        if (state is ArrivalState.OutsideObserved or ArrivalState.Attempted && !current.Enabled) throw new LocationReminderConflictException();
        if (state == ArrivalState.Attempted && current.State != ArrivalState.OutsideObserved) throw new LocationReminderConflictException();
        var changed = current with { Revision = checked(current.Revision + 1), State = state, Status = status };
        Write(connection, transaction, changed, key); return changed;
    });
    public Task DeleteAsync(LocationReminder expected) => ExecuteAsync(expected.Scope, (connection, transaction, key) =>
    {
        var current = Read(connection, transaction, expected.Scope, key).SingleOrDefault(r => r.Id == expected.Id);
        if (current != expected || current.Enabled) throw new LocationReminderConflictException();
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "DELETE FROM LocationReminders WHERE Scope=$scope AND Id=$id;";
        command.Parameters.AddWithValue("$scope", expected.Scope); command.Parameters.AddWithValue("$id", expected.Id.ToString("N")); command.ExecuteNonQuery(); return true;
    });
    private static IReadOnlyList<LocationReminder> Read(SqliteConnection connection, SqliteTransaction transaction, string scope, byte[] key)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Id,Revision,NotificationId,Data FROM LocationReminders WHERE Scope=$scope;"; command.Parameters.AddWithValue("$scope", scope);
        using var reader = command.ExecuteReader(); var result = new List<LocationReminder>();
        while (reader.Read())
        {
            var id = reader.GetString(0); var revision = reader.GetInt64(1); var native = reader.GetInt32(2);
            var parts = reader.GetString(3).Split('.');
            try
            {
                if (parts.Length != 4 || parts[0] != "v1") throw new CryptographicException();
                var encrypted = Convert.FromBase64String(parts[3]); if (encrypted.Length > 32768) throw new CryptographicException();
                var plain = new byte[encrypted.Length]; using var cipher = new AesGcm(key, 16);
                cipher.Decrypt(Convert.FromBase64String(parts[1]), encrypted, Convert.FromBase64String(parts[2]), plain, Aad(scope, id, revision, native));
                var row = JsonSerializer.Deserialize<LocationReminder>(plain) ?? throw new InvalidDataException();
                if (row.Id == Guid.Empty || row.Id.ToString("N") != id || row.Scope != scope || row.Revision != revision || revision <= 0
                    || row.NotificationId != native || native >= 0 || row.ArmId == Guid.Empty || !Enum.IsDefined(row.State) || row.Recurrence is null
                    || string.IsNullOrWhiteSpace(row.TaskTitle) || row.TaskTitle.Length > 200 || row.Status is null || row.Status.Length > 500) throw new InvalidDataException();
                LocationRules.Validate(new(row.TaskId, row.PlaceName, row.Latitude, row.Longitude, row.RadiusMeters)); RecurrenceRules.Validate(row.Recurrence);
                result.Add(row);
            }
            catch (Exception error) when (error is CryptographicException or FormatException or JsonException or ArgumentException or InvalidDataException)
            { throw new InvalidDataException("Location data could not be authenticated/read. Keep the original protected key and database; nothing was reset."); }
        }
        return result.OrderBy(r => r.TaskTitle, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id).ToArray();
    }
    private static byte[] Aad(string scope, string id, long revision, int native) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { scope, id, revision, native }));
    private static void Write(SqliteConnection connection, SqliteTransaction transaction, LocationReminder row, byte[] key)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(row); var encrypted = new byte[plain.Length]; var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
        using (var cipher = new AesGcm(key, 16)) cipher.Encrypt(nonce, plain, encrypted, tag, Aad(row.Scope, row.Id.ToString("N"), row.Revision, row.NotificationId));
        var data = "v1." + Convert.ToBase64String(nonce) + "." + Convert.ToBase64String(tag) + "." + Convert.ToBase64String(encrypted);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocationReminders(Id,Scope,Revision,NotificationId,Data) VALUES ($id,$scope,$revision,$native,$data)
            ON CONFLICT(Id) DO UPDATE SET Revision=$revision,Data=$data WHERE LocationReminders.Scope=$scope;
            """;
        command.Parameters.AddWithValue("$id", row.Id.ToString("N")); command.Parameters.AddWithValue("$scope", row.Scope);
        command.Parameters.AddWithValue("$revision", row.Revision); command.Parameters.AddWithValue("$native", row.NotificationId); command.Parameters.AddWithValue("$data", data);
        if (command.ExecuteNonQuery() != 1) throw new LocationReminderConflictException();
    }
    private Task<T> ExecuteAsync<T>(string scope, Func<SqliteConnection, SqliteTransaction, byte[], T> action) => Task.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 2048 || scope.Any(char.IsControl)) throw new ArgumentException("Choose a valid location workspace.");
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = full, Pooling = false, DefaultTimeout = 5 }.ToString()); await connection.OpenAsync();
        using var transaction = connection.BeginTransaction(deferred: false); using var schema = connection.CreateCommand(); schema.Transaction = transaction;
        schema.CommandText = "PRAGMA user_version;"; var version = Convert.ToInt32(schema.ExecuteScalar());
        if (version is < 0 or > 1) throw new InvalidDataException("Location reminders require a compatible app. Nothing was reset.");
        if (version == 0)
        {
            schema.CommandText = """
                CREATE TABLE LocationMetadata(Id INTEGER PRIMARY KEY CHECK(Id=1),KeyHash TEXT,NextId INTEGER NOT NULL CHECK(NextId<0));
                INSERT INTO LocationMetadata(Id,NextId) VALUES(1,-1);
                CREATE TABLE LocationReminders(Id TEXT PRIMARY KEY,Scope TEXT NOT NULL,Revision INTEGER NOT NULL CHECK(Revision>0),NotificationId INTEGER NOT NULL UNIQUE CHECK(NotificationId<0),Data TEXT NOT NULL);
                CREATE INDEX IX_LocationReminders_Scope ON LocationReminders(Scope); PRAGMA user_version=1;
                """; schema.ExecuteNonQuery();
        }
        schema.CommandText = "SELECT KeyHash FROM LocationMetadata WHERE Id=1;"; var fingerprint = schema.ExecuteScalar() as string;
        schema.CommandText = "SELECT count(*) FROM LocationMetadata WHERE Id=1 AND NextId<0;";
        if (Convert.ToInt32(schema.ExecuteScalar()) != 1) throw new InvalidDataException("Location metadata is invalid. Nothing was reset.");
        schema.CommandText = "SELECT count(*) FROM LocationReminders;";
        if (fingerprint is null && Convert.ToInt32(schema.ExecuteScalar()) != 0) throw new InvalidDataException("The original location key fingerprint is missing. Nothing was reset.");
        var key = await keys.GetAsync(allowCreate: fingerprint is null);
        if (key.Length != 32) throw new InvalidDataException("The protected location key is invalid. Nothing was reset.");
        var hash = Convert.ToHexString(SHA256.HashData(key));
        if (fingerprint is not null && fingerprint != hash) throw new InvalidDataException("The original protected location key is unavailable. Nothing was reset.");
        if (fingerprint is null) { schema.CommandText = "UPDATE LocationMetadata SET KeyHash=$hash WHERE Id=1;"; schema.Parameters.AddWithValue("$hash", hash); schema.ExecuteNonQuery(); }
        var result = action(connection, transaction, key); transaction.Commit(); return result;
    });
}
