# Database plan

## Current state

Phase 4 implements SqliteLocalTaskService using Microsoft.Data.Sqlite 10.0.15. Task/category data is stored in a file, and Phase 5 task ViewModels consume this service. Opening the Windows list and file-reopening persistence tests have been verified; full app restart acceptance remains on the README checklist. Phase 6 adds separate Development-only in-memory API tasks/categories. No server database, EF DbContext, or PostgreSQL migrations exist yet.

## Initial entities and relationship

```text
Category (1) ---- (0..many) TaskItem
TaskItem.CategoryId is optional: uncategorized tasks are valid.
```

TaskItem fields: client-generated `Guid Id`, required trimmed `Title`, optional `Description`, `bool IsCompleted`, optional `DateOnly DueDate`, enum `Priority`, optional `Guid CategoryId`, and UTC `DateTimeOffset CreatedAt/UpdatedAt`. Date-only due dates avoid changing the intended day across time zones. Reminders with a time will need a separate design.

Category has a generated `Guid Id`, required trimmed `Name`, and a read-only task navigation collection. TaskItem.SetCategory keeps both sides consistent and supports clearing the category. CategoryId is computed from the in-memory reference. SQLite stores CategoryId as a nullable foreign key; loading restores a shared category object per ID and attaches its tasks. Internal domain Restore factories preserve saved IDs and timestamps. Infrastructure receives access with InternalsVisibleTo, rather than public identity setters or storage attributes in Domain.

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

Current Phase 6 endpoints use API/Services/InMemoryTaskService. It holds data for one API process only, locks access across requests, and returns flat DTO snapshots. Server CategoryName values are matched case-insensitively; server IDs/timestamps are generated independently from local tasks. There is no client/server ID mapping or sync yet, and the API never opens the MAUI SQLite file. Restarting the API resets only the temporary server data.

Phase 7 must replace that storage while retaining the tested REST contract: 201/Location creation, 200 full updates (including completion), 204 deletion, 400 validation, 404 missing tasks, server-owned identity/timestamps, and named priority JSON. Preserve task/category identity on round trips, define server category-name uniqueness deliberately, and keep database configuration outside controllers. Authentication/production deployment remain later gates.

When the server persistence phase begins:

1. Install PostgreSQL locally or use a development instance; create a development database and least-privileged development role.
2. Add a stable compatible EF Core 10 release, matching EF Design tooling, and an Npgsql EF provider whose documented dependency range supports that EF version. Check current versions at that time.
3. Add a server DbContext, entity configurations, relationships, constraints, and host DI registration.
4. Initialize development user secrets for TodoApp.Api. Set `ConnectionStrings:TodoDatabase` there, or use the environment variable `ConnectionStrings__TodoDatabase`.
5. Generate and review an initial Code First migration with Infrastructure as the migrations project and API as the startup project; apply it explicitly to the development database.

Example future secret value format (placeholders only):

```text
Host=localhost;Port=5432;Database=todoapp;Username=<dev-role>;Password=<local-secret>
```

Future command shape, valid only after EF packages, the local tool, DbContext, and configuration exist:

```powershell
dotnet ef migrations add InitialCreate --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api
```

Do not run these now. Never store production credentials in appsettings, source, examples, or MAUI. Review migrations before release; apply production migrations through a controlled deployment step rather than automatic startup mutation. Back up data before destructive schema changes.

## Future synchronization

Local and server stores share logical task/category IDs but do not directly connect to one another. Only the API connects to PostgreSQL. Synchronization will need a transactional local outbox, deletion tombstones, server revisions, incremental download cursors, idempotency, retries, and explicit conflict resolution. UpdatedAt alone is insufficient for conflict ordering. See [ARCHITECTURE.md](ARCHITECTURE.md).

Authentication will add user ownership and server authorization before multi-user sync is released. Add each schema change through reviewed server migrations and local upgrades. SQLite files are not automatically encrypted; evaluate data protection when sensitive data and login arrive.
