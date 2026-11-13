# Database plan

## Current state

Local tasks use SQLite through SqliteLocalTaskService. Phase 7 adds separate PostgreSQL storage for API tasks using EF Core and TodoDbContext. The initial migration and nine real PostgreSQL test cases verify mapping, retained data across API hosts, constraints, rollback, concurrency, and relationships. Your existing PostgreSQL service/databases were not changed during verification; tests used an isolated project-local cluster. Configure your own development connection and apply the migration before running the normal API.

## Initial entities and relationship

```text
Category (1) ---- (0..many) TaskItem
TaskItem.CategoryId is optional: uncategorized tasks are valid.
```

TaskItem fields: client-generated `Guid Id`, required trimmed `Title`, optional `Description`, `bool IsCompleted`, optional `DateOnly DueDate`, enum `Priority`, optional `Guid CategoryId`, and UTC `DateTimeOffset CreatedAt/UpdatedAt`. Date-only due dates avoid changing the intended day across time zones. Reminders with a time will need a separate design.

Category has a generated Guid Id, required trimmed Name, and a read-only task collection. TaskItem.SetCategory maintains a scalar CategoryId alongside its navigation reference, including clearing an ID when EF has not loaded the navigation. IDs/creation times have private setters and private EF materialization constructors; normal creation still uses validating constructors. SQLite retains its internal Restore path without a schema change. Domain has no EF package dependency or storage attributes; Fluent configurations live in Infrastructure.

Implemented rules:

- Title: 1–200 characters after trimming. Description: optional, at most 4000 characters after trimming; whitespace-only becomes null.
- Category name: 1–100 characters after trimming. Duplicate category names are currently allowed; no uniqueness policy has been introduced.
- Priority: Low = 0, Normal = 1 (default), High = 2. Undefined enum values are rejected.
- Tasks start pending. SetCompleted supports completion/reopening; assigning the existing state does not change UpdatedAt.
- UpdateDetails validates before mutation. IDs/CreatedAt stay fixed, and effective task edits update UpdatedAt with DateTimeOffset.UtcNow. Equivalent normalized edits are no-ops.
- DueDate can be absent or in the past. It represents a calendar day, not a time zone or scheduled reminder.

These timestamps describe local edits, not server revision ordering. SQLite materialization restores them without recording a new edit. Server timestamp ownership for incoming DTOs remains future work.

SQLite configures and tests category deletion with ON DELETE SET NULL, preserving tasks. There is no category delete/rename service or UI yet. Parent tasks, dependencies, recurrence, users, and synchronization metadata are excluded from this initial schema.

## SQLite: local device storage

The selected [Microsoft.Data.Sqlite 10.0.15 package](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.15) restores successfully against net10.0 and bundles native SQLite. No separate database-server installation is needed. MAUI selects `Path.Combine(FileSystem.AppDataDirectory, "todoapp.db3")`, outside the checkout; tests supply unique temporary paths. Opening the task list performs the first query, creating the directory/file and schema version 1 when absent. SQLite data/journal files are ignored by Git. Windows native build/initial access passes; mobile packaging remains unverified.

The editor keeps title/description/category/date/priority fields separate from loaded entities until Save. It matches an entered category name case-insensitively against categories visible among existing tasks; new names create a new category on save. Blank category clears the association. Empty stored categories have no browsing/management UI yet. Cancel/Back discards fields without a write. Completion is changed from the list separately, and deleting a task requires a native confirmation.

The Phase 3 contract is preserved: null/false for missing tasks, domain validation, explicit completion changes, full detail replacement, optional category assignment, cancellation checks, and the five filters with local-day semantics. Both stores now share TaskQueries for filtering/ordering. SQLite returns detached entities with read-only collection membership. Only explicit service writes save changes; refresh to see new state. Old snapshots are not changed by subsequent writes/deletions. Category input objects are not mutated or tracked.

### Version-1 schema and mapping

| Table | Stored columns |
| --- | --- |
| Categories | Id TEXT primary key, Name TEXT required (1–100 trimmed characters) |
| TaskItems | Id TEXT primary key, Title TEXT required (1–200 trimmed characters), Description nullable TEXT (up to 4000), IsCompleted INTEGER (0/1), DueDate nullable TEXT, Priority INTEGER (0/1/2), CategoryId nullable TEXT FK, CreatedAt/UpdatedAt required TEXT |

IDs use Guid's string representation. DueDate uses invariant `yyyy-MM-dd`; UTC timestamps use round-trip `O` format. CategoryId is indexed, references Categories.Id, and uses ON DELETE SET NULL. Foreign keys are enabled on every connection. Domain validation checks inputs; SQLite constraints provide an additional guard. All caller values go through SQL parameters, not string-built SQL.

Creating or updating a task inserts a new category in the same transaction when needed. Existing category IDs retain their stored names so stale input cannot overwrite them. Updating a category's name through a detached object is not persisted; category management needs a separate future operation. Deleting a task does not delete an otherwise empty category.

### Transactions, cancellation, and failure behavior

Each service call opens/disposes a connection, checks the schema, and runs its operation in a transaction. Writes commit only after all statements succeed and a final cancellation check passes. Failure rolls back category/task changes together and propagates to the caller. No optimistic UI mutation is performed inside the storage service. Separate-instance concurrent creation and forced SQL failure/rollback are covered by file-based tests.

Microsoft.Data.Sqlite's async APIs run synchronously ([official limitation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)). Task.Run moves each short operation off the future UI thread. Cancellation is cooperative before opening, between loaded rows, and before commits; it cannot instantly interrupt native statements or busy waits. Default busy timeout is five seconds. Read/modify/write transactions serialize local operations; they do not implement synchronization conflicts.

### Schema version and upgrade strategy

`PRAGMA user_version` is 0 for a new SQLite file. SqliteTaskDatabase creates both tables/index and changes it to 1 in one transaction. Version 1 is reopened without recreating tables or deleting data. A version above 1 is rejected with a clear exception, preserving that database's version/data. Existing conflicting unversioned tables produce an error rather than a reset.

Only initialization to version 1 exists today. Before any schema change, add an explicit incremental migration for that old version, update the supported version, and test it using a database created by the earlier schema with real rows. Keep each migration transactional and advance user_version only on success. Never drop/reset user data as an upgrade shortcut. Server EF migrations are a separate mechanism.

## PostgreSQL: server storage

Normal endpoints use scoped PostgresTaskService through ITaskService. TodoDbContext and its configurations/migrations live under Infrastructure/Persistence/Server. API/Configuration registers the context/service and loads the connection through configuration. The old InMemoryTaskService remains only as an explicit test substitute; startup never silently chooses it.

### Packages and server mapping

EF Core, Relational, Design, and dotnet-ef are pinned to 10.0.15. Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 [supports EF Core >=10.0.4 and <11](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3). Explicit Relational pinning prevents different patch assemblies being chosen through Design versus the provider. Design tooling is private to the API project; the local tool manifest is dotnet-tools.json.

| Table | Main mapping |
| --- | --- |
| Categories | uuid Id, varchar(100) Name, required varchar(100) shadow NameKey, unique NameKey index, nonblank-name check |
| Tasks | uuid Id, varchar(200) Title, nullable varchar(4000) Description, boolean IsCompleted, nullable date DueDate, integer Priority, nullable uuid CategoryId, UTC timestamptz CreatedAt/UpdatedAt |

IDs are assigned by the domain (ValueGeneratedNever). Priority is constrained to 0/1/2; titles must be nonblank; CategoryId is indexed and uses ON DELETE SET NULL. EF accesses Category's private tasks backing field. Read queries use Include for category names and AsNoTracking for DTO reads; writes use a request-scoped tracked context.

Category NameKey is the trimmed name converted with ToUpperInvariant, stored as an EF shadow property rather than a domain field. A unique index enforces server case-normalized name reuse. ResolveCategory uses one parameterized INSERT ... ON CONFLICT DO NOTHING followed by an EF query, inside the task transaction. Concurrent requests reuse the winning category ID/name instead of racing to insert duplicates. Category rename is not exposed; any future implementation must update NameKey with Name. SQLite's duplicate-name policy is unchanged.

PostgreSQL stores timestamp microseconds; .NET ticks have finer precision. SaveChangesAsync normalizes task timestamps to UTC microseconds before saving/returning DTOs, so POST/PUT responses match subsequent reads exactly. Dates remain date-only values. Create/update commit category/task changes together; failure disposes the uncommitted transaction. Native async EF/Npgsql calls receive cancellation tokens; no Task.Run wrapper is used. Concurrent edits currently use last-writer behavior, without a revision/conflict protocol.

### Configure your development database

The installed PostgreSQL 18 Windows service was detected running. Its credentials, roles, and databases were not inspected or altered. Use pgAdmin or psql as an administrator to create a new dedicated development role/database. Example psql commands (choose different names if these already exist):

```sql
CREATE ROLE todoapp_dev LOGIN;
\password todoapp_dev
CREATE DATABASE todoapp OWNER todoapp_dev;
```

The psql password command prompts locally; do not paste credentials into chat or commit them. The role owns only the chosen development database and does not need superuser privileges. Then, from the solution root in PowerShell, replace the placeholder with your local password:

```powershell
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet user-secrets set "ConnectionStrings:TodoDatabase" "Host=localhost;Port=5432;Database=todoapp;Username=todoapp_dev;Password=<local-password>" --project src/TodoApp.Api
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
dotnet run --project src/TodoApp.Api --launch-profile https
```

UserSecretsId is already assigned. Alternatively set ConnectionStrings__TodoDatabase in the API terminal or deployment configuration. A connection string is required at startup; missing configuration stops startup with instructions. No credentials are in appsettings/source. The API stays Development-only until authentication/user ownership are implemented. The health endpoint is liveness, not a database readiness check.

### Migration strategy

The generated initial migration is `20261113005745_InitialCreate` in Persistence/Server/Migrations, alongside its designer and model snapshot. It creates Categories/Tasks, constraints, and indexes. Review it and apply it explicitly with database update; the API does not call EnsureCreated or Migrate during startup.

For future model changes:

```powershell
dotnet ef migrations add MeaningfulChangeName --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext --output-dir Persistence/Server/Migrations
dotnet ef migrations script --idempotent --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
```

Do not regenerate InitialCreate against an existing database. Review changes before applying them, and use controlled deployment/backup procedures before any destructive future migration. PostgreSQL tracks applied EF migrations in __EFMigrationsHistory; SQLite's user_version mechanism is separate.

### Real PostgreSQL tests

Default dotnet test runs the portable suite using an explicit in-memory REST substitute and skips nine database cases unless TODOAPP_TEST_POSTGRES is configured. It does not pretend those skipped cases validate PostgreSQL. To run all cases, provide a dedicated development test connection whose role has CREATEDB permission:

```powershell
$env:TODOAPP_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=<test-role>;Password=<test-password>'
.\scripts\Test-Postgres.ps1
```

Each database test creates/migrates a uniquely named todoapp_tests_<guid> database, then drops only that database during cleanup. Never point this at production. The script builds into ignored .tools/postgres-checks so an existing dev API need not lock its outputs. Tests cover retained DTOs across new API hosts, identity/timestamp precision, full CRUD, category concurrency/uniqueness, FK deletion/clearing, rollback after forced failure, filters, validation, and repeatable migrations/model consistency.

This run used PostgreSQL 18 binaries with a separate .tools/pg-tests cluster on 127.0.0.1:55439 and a loopback-only trust-auth test role. This was test infrastructure, not the app's development database configuration or a replacement for your running service. Test databases were cleaned up; the isolated server is stopped after verification. Its local files remain ignored. Your normal API still needs the development connection/migration steps above.

## Future synchronization

Local and server stores share logical task/category IDs but do not directly connect to one another. Only the API connects to PostgreSQL. Synchronization will need a transactional local outbox, deletion tombstones, server revisions, incremental download cursors, idempotency, retries, and explicit conflict resolution. UpdatedAt alone is insufficient for conflict ordering. See [ARCHITECTURE.md](ARCHITECTURE.md).

Authentication will add user ownership and server authorization before multi-user sync is released. Add each schema change through reviewed server migrations and local upgrades. SQLite files are not automatically encrypted; evaluate data protection when sensitive data and login arrive.
