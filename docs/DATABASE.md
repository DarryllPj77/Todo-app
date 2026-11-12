# Database plan

## Current state

Phase 2 implements TaskItem, Category, and TaskPriority in Domain. There are still no persistence packages, connection strings, DbContext, databases, or migrations. Entity rules below are implemented; database mapping and setup remain planned.

## Initial entities and relationship

```text
Category (1) ---- (0..many) TaskItem
TaskItem.CategoryId is optional: uncategorized tasks are valid.
```

TaskItem fields: client-generated `Guid Id`, required trimmed `Title`, optional `Description`, `bool IsCompleted`, optional `DateOnly DueDate`, enum `Priority`, optional `Guid CategoryId`, and UTC `DateTimeOffset CreatedAt/UpdatedAt`. Date-only due dates avoid changing the intended day across time zones. Reminders with a time will need a separate design.

Category has a generated `Guid Id`, required trimmed `Name`, and a read-only task navigation collection. TaskItem.SetCategory keeps both sides consistent and supports clearing the category. CategoryId is currently computed from the in-memory reference, not an independently persisted foreign key. Before persistence, introduce explicit mapping/loading support (including scalar FK storage as needed) and test round trips; these plain classes are not claimed to be ready-made EF/SQLite mappings.

Implemented rules:

- Title: 1–200 characters after trimming. Description: optional, at most 4000 characters after trimming; whitespace-only becomes null.
- Category name: 1–100 characters after trimming. Duplicate category names are currently allowed; no uniqueness policy has been introduced.
- Priority: Low = 0, Normal = 1 (default), High = 2. Undefined enum values are rejected.
- Tasks start pending. SetCompleted supports completion/reopening; assigning the existing state does not change UpdatedAt.
- UpdateDetails validates before mutation. IDs/CreatedAt stay fixed, and effective task edits update UpdatedAt with DateTimeOffset.UtcNow. Equivalent normalized edits are no-ops.
- DueDate can be absent or in the past. It represents a calendar day, not a time zone or scheduled reminder.

These timestamps describe local in-memory edits, not server revision ordering. The database/service phases must define materialization of persisted IDs/timestamps without creating replacement identities, and server timestamp ownership for incoming DTOs. No persistence constructors or mapping attributes are added speculatively.

Deleting a category should preserve its tasks and clear their category reference. Confirm the behavior with tests and explicit FK configuration when implementing persistence. Parent tasks, dependencies, recurrence, users, and synchronization metadata are excluded from this initial schema.

## SQLite: local device storage

Choose a maintained .NET 10-compatible SQLite library during the local storage phase. Store the database under MAUI's application data directory, not in the checkout. Keep local mapping, initialization, transactions, and upgrade logic behind a small service/repository, separate from ViewModels. Do not assume a server EF migration can be reused against a different local persistence library.

Test that tasks survive restart and that failed writes do not silently lose UI changes. Define a local schema version and an incremental upgrade path before changing an existing user's database. Never reset user data as a normal upgrade strategy.

## PostgreSQL: server storage

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
