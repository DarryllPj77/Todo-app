using Microsoft.Data.Sqlite;

namespace TodoApp.Infrastructure.Persistence;

internal sealed class SqliteTaskDatabase
{
    private readonly string databasePath;

    public SqliteTaskDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = Path.GetFullPath(databasePath);
    }

    public SqliteConnection OpenConnection(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());

        try
        {
            connection.Open();
            InitializeSchema(connection, cancellationToken);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void InitializeSchema(SqliteConnection connection, CancellationToken cancellationToken)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(command.ExecuteScalar());

        if (version > 3)
        {
            throw new InvalidOperationException("This database was created by a newer app. Update the app before opening it.");
        }

        if (version == 0)
        {
            command.CommandText = """
                CREATE TABLE Categories (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 100)
                );
                CREATE TABLE TaskItems (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Title TEXT NOT NULL CHECK(length(trim(Title)) BETWEEN 1 AND 200),
                    Description TEXT NULL CHECK(Description IS NULL OR length(Description) <= 4000),
                    IsCompleted INTEGER NOT NULL CHECK(IsCompleted IN (0, 1)),
                    DueDate TEXT NULL,
                    Priority INTEGER NOT NULL CHECK(Priority IN (0, 1, 2)),
                    CategoryId TEXT NULL REFERENCES Categories(Id) ON DELETE SET NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE INDEX IX_TaskItems_CategoryId ON TaskItems(CategoryId);
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        if (version < 2)
        {
            command.CommandText = "ALTER TABLE TaskItems ADD COLUMN Tags TEXT NOT NULL DEFAULT '[]'; PRAGMA user_version=2;";
            command.ExecuteNonQuery();
        }

        if (version < 3)
        {
            command.CommandText = """
                ALTER TABLE TaskItems ADD COLUMN ParentTaskId TEXT NULL REFERENCES TaskItems(Id) ON DELETE RESTRICT
                    CHECK(ParentTaskId IS NULL OR ParentTaskId<>Id);
                CREATE INDEX IX_TaskItems_ParentTaskId ON TaskItems(ParentTaskId);
                PRAGMA user_version=3;
                """;
            command.ExecuteNonQuery();
        }

        // Future upgrades go here as explicit version-to-version migrations.
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }
}
