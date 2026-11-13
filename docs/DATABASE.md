# Database plan

## Current state

Device tasks use todoapp.db3 schema **2** with normalized tag JSON. The API has four migrations: InitialCreate, AddAuthenticationOwnership, AddTaskSynchronization, and AddTaskTags. The last adds required Tasks.Tags text defaulting to []. sync.db3 and views.db3 use schema **2** for tag-aware JSON; reminders.db3 stays at **1**. Existing local rows, account ownership, queues, receipts, and saved definitions are retained. Verification used isolated test databases, not your development database. Follow TAGS.md to stop/upgrade the API and client together.

## Initial entities and relationship

```text
Category (1) ---- (0..many) TaskItem
TaskItem.CategoryId is optional: uncategorized tasks are valid.
```

TaskItem fields: client-generated `Guid Id`, required trimmed `Title`, optional `Description`, `bool IsCompleted`, optional `DateOnly DueDate`, enum `Priority`, optional `Guid CategoryId`, and UTC `DateTimeOffset CreatedAt/UpdatedAt`. Date-only due dates avoid changing the intended day across time zones. Reminders with a time will need a separate design.

Category has a generated Guid Id, required trimmed Name, and a read-only task collection. TaskItem.SetCategory maintains a scalar CategoryId alongside its navigation reference, including clearing an ID when EF has not loaded the navigation. IDs/creation times have private setters and private EF materialization constructors; normal creation still uses validating constructors. SQLite retains its internal Restore path without a schema change. Domain has no EF package dependency or storage attributes; Fluent configurations live in Infrastructure.

Implemented rules:

- Title: 1–200 characters after trimming. Description: optional, at most 4000 characters after trimming; whitespace-only becomes null.
- Category name: 1–100 characters after trimming. The domain constructor allows repeated names; local services reuse case-insensitive names, and the server enforces normalized uniqueness per owner.
- Priority: Low = 0, Normal = 1 (default), High = 2. Undefined enum values are rejected.
- Tasks start pending. SetCompleted supports completion/reopening; assigning the existing state does not change UpdatedAt.
- UpdateDetails validates before mutation. IDs/CreatedAt stay fixed, and effective task edits update UpdatedAt with DateTimeOffset.UtcNow. Equivalent normalized edits are no-ops.
- DueDate can be absent or in the past. It represents a calendar day, not a time zone or scheduled reminder.

These timestamps describe local edits, not server revision ordering. SQLite materialization restores them without recording a new edit. Server timestamp ownership for incoming DTOs remains future work.

SQLite configures and tests category deletion with ON DELETE SET NULL, preserving tasks. There is no category delete/rename service or UI yet. Parent tasks, dependencies, recurrence, users, and synchronization metadata are excluded from this initial schema.

## SQLite: local device storage

The selected [Microsoft.Data.Sqlite 10.0.15 package](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.15) restores successfully against net10.0 and bundles native SQLite. No separate database-server installation is needed. MAUI selects `Path.Combine(FileSystem.AppDataDirectory, "todoapp.db3")`, outside the checkout; tests supply unique temporary paths. Opening the task list performs the first query, creating the directory/file and current schema version 2 when absent (initial creation followed by the explicit tag upgrade). SQLite data/journal files are ignored by Git. Windows native build/initial access passes; mobile packaging remains unverified.

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

Initialization plus the tested version-1-to-2 tag upgrade exist today. Future schema changes need explicit incremental migrations, supported-version updates, and tests against earlier schemas with real rows. Keep each migration transactional and advance user_version only on success. Never drop/reset user data as an upgrade shortcut. Server EF migrations are a separate mechanism.

## PostgreSQL: server storage

Normal endpoints use scoped PostgresTaskService through ITaskService. TodoDbContext and its configurations/migrations live under Infrastructure/Persistence/Server. API/Configuration registers the context/service and loads the connection through configuration. The old InMemoryTaskService remains only as an explicit test substitute; startup never silently chooses it.

### Packages and server mapping

EF Core, Relational, Design, and dotnet-ef are pinned to 10.0.15. Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 [supports EF Core >=10.0.4 and <11](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3). Explicit Relational pinning prevents different patch assemblies being chosen through Design versus the provider. Design tooling is private to the API project; the local tool manifest is dotnet-tools.json.

| Table | Main mapping |
| --- | --- |
| Users | uuid Id, varchar(254) Email/NormalizedEmail, unique NormalizedEmail, required varchar(1024) PasswordHash |
| Categories | uuid Id, varchar(100) Name, required shadow NameKey, nullable legacy shadow OwnerId, unique (OwnerId, NameKey), nonblank-name check |
| Tasks | uuid Id, varchar(200) Title, nullable varchar(4000) Description, boolean IsCompleted, nullable date DueDate, integer Priority, nullable uuid CategoryId/OwnerId, UTC timestamptz CreatedAt/UpdatedAt |

IDs are assigned by the domain (ValueGeneratedNever). Priority is constrained to 0/1/2; titles must be nonblank; CategoryId is indexed and uses ON DELETE SET NULL. EF accesses Category's private tasks backing field. Read queries use Include for category names and AsNoTracking for DTO reads; writes use a request-scoped tracked context.

Category NameKey is the trimmed name converted with ToUpperInvariant. The unique (OwnerId, NameKey) index enforces normalized name reuse per account. ResolveCategory includes the validated current owner in both its parameterized upsert and EF query. Two users may independently have Work; concurrent requests within one account reuse the winning category. Rename is not exposed and would need to update NameKey with Name. SQLite's duplicate-name policy is unchanged.

Server ownership uses shadow OwnerId fields referencing Users with restricted user deletion. Every public task query/update/delete is owner-filtered; new task/category writes take ownership only from the validated sub claim. The DTO accepts no owner ID. CategoryId still references Categories with ON DELETE SET NULL. Ownership is enforced in the service; direct administrative SQL must preserve task/category owner consistency.

### Existing anonymous data

AddAuthenticationOwnership preserves existing tasks/categories with null OwnerId. Authenticated queries exclude those rows, and registering a new account never claims them. If you need them, an administrator must explicitly map selected legacy tasks and their categories to a verified account, reconcile per-account category-name collisions, and review the assignment. No blanket first-user assignment or data-reset script is provided. Local MAUI SQLite data is unaffected and remains device-local. Phase 9 keeps it separate from each online server account; no automatic assignment, upload, or schema change occurs.

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
$jwtKeyBytes = New-Object byte[] 32
$jwtRandom = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtRandom.GetBytes($jwtKeyBytes)
$jwtRandom.Dispose()
dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($jwtKeyBytes)) --project src/TodoApp.Api
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
dotnet run --project src/TodoApp.Api --launch-profile https
```

UserSecretsId is already assigned. Alternatively use ConnectionStrings__TodoDatabase and Jwt__SigningKey in the API terminal. Jwt:SigningKey must decode to at least 32 random bytes; no fallback key is generated. Public issuer/audience/15-minute lifetime defaults are in appsettings, and the accepted lifetime range is 1–60 minutes. Do not regenerate the signing secret on every startup; replacing it and restarting the API invalidates old tokens. Missing/invalid configuration stops startup. No credentials/signing key are in committed appsettings/source. The API remains Development-only pending deployment hardening/acceptance. Health is liveness, not readiness.

After startup, register or log in using the HTTPS auth endpoints, then send the returned bearer token on task requests. Passwords are hashed using ASP.NET Core PasswordHasher; plaintext passwords and hashes are never returned in account DTOs. Login failures use the same public response for wrong/unknown credentials. Password reset/email verification/refresh tokens are outside this learning phase; access tokens expire and require re-login. Local token removal is not immediate server-side revocation.

### Migration strategy

The generated migrations are 20261113005745_InitialCreate, 20261113011803_AddAuthenticationOwnership, 20261113073352_AddTaskSynchronization, and 20261113092317_AddTaskTags in Persistence/Server/Migrations, with designers and model snapshot. AddAuthenticationOwnership creates Users, adds nullable legacy owners/FKs, and replaces global category-name uniqueness with per-owner uniqueness. AddTaskSynchronization creates the sync records/receipts described below. Apply pending migrations explicitly with database update; the API does not call EnsureCreated or Migrate during startup.

For future model changes:

```powershell
dotnet ef migrations add MeaningfulChangeName --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext --output-dir Persistence/Server/Migrations
dotnet ef migrations script --idempotent --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
```

Do not regenerate InitialCreate or downgrade the ownership migration against live multi-user data: its Down method removes accounts/owner columns and cannot restore global category-name uniqueness if users share names. Review migrations with backup/roll-forward planning. PostgreSQL tracks applied EF migrations in __EFMigrationsHistory; SQLite's user_version mechanism is separate.

### Real PostgreSQL tests

Default dotnet test uses explicit in-memory REST/test-auth substitutes and skips 32 database cases unless TODOAPP_TEST_POSTGRES is configured. It does not treat fake authentication as JWT validation. Real auth tests use the normal JWT handler and scoped PostgreSQL service. To run all cases, provide a dedicated development test connection whose role has CREATEDB permission:

```powershell
$env:TODOAPP_TEST_POSTGRES = 'Host=localhost;Port=5432;Database=postgres;Username=<test-role>;Password=<test-password>'
.\scripts\Test-Postgres.ps1
```

Each database test creates/migrates a unique todoapp_tests_<guid> database and drops only that database during cleanup. Never point this at production. The script builds into ignored .tools/postgres-checks. It covers stored data/transactions plus registration, hashes, email uniqueness, generic failed login, rate limiting, real invalid/expired JWT rejection, account deletion, user/category isolation, spoofed input, and preservation of anonymous rows during migration. Test signing keys are generated in process and not printed or committed.

This run used PostgreSQL 18 binaries with a separate .tools/pg-tests cluster on 127.0.0.1:55439 and a loopback-only trust-auth test role. This was test infrastructure, not the app's development database configuration or a replacement for your running service. Test databases were cleaned up; the isolated server is stopped after verification. Its local files remain ignored. Your normal API still needs the development connection/migration steps above.

## Synchronization storage (Phase 10)

AddTaskSynchronization creates SyncRecords (composite account/task key, unique account/revision index, latest payload or tombstone) and SyncReceipts (composite account/operation key, immutable request/result). Both have account FKs; tombstones deliberately have no FK to a live task. Task changes, journal revision, and receipt are one transaction. Existing owned rows are bootstrapped lazily under the account lock; anonymous rows are never assigned. The Down migration removes sync history/receipts and is unsafe against active replicas. Review backup/roll-forward recovery before any downgrade; never prune receipts/tombstones without a retention/reset design.

The separate `sync.db3` file now has independent schema version 2 and ReplicaPartitions rows keyed by canonical API origin/account ID. Each state includes cached local intent, pending operations, latest server revisions, tombstones/conflicts, retry metadata, and cursor. Saving an edit queues its operation atomically. Applying a download commits rows and cursor atomically. Corrupt/newer-version caches fail without a data reset. This compact JSON-per-partition implementation targets the small MVP dataset; normalization/paging is later performance work.

The existing `todoapp.db3` version-1 device-only tables are untouched. Server credentials and signing keys never enter either client file. Task cache content is unencrypted; tokens remain in platform SecureStorage. Logout hides the account cache without deleting queued work. Database restore, raw SQL edits to journaled tasks, and lost server history need explicit reconciliation; do not delete a cache containing pending work to reset its cursor. See [SYNCHRONIZATION.md](SYNCHRONIZATION.md).

To upgrade your development database, stop the API and run the existing `dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext` command with your configured Development secrets, then restart HTTPS. No automatic startup migration runs. Verification applies migrations only to generated test databases; your development database was not migrated by this work.

## Phase 9 client storage boundary

The Account page reaches PostgreSQL only through the authenticated HTTPS API. Its server rows are memory-only; local SQLite remains the device task store. There is no download cache, outbox, tombstone, or automatic import. Account/address changes clear server rows/drafts without deleting device tasks.

The protected session envelope contains API origin, bearer token, expiry, and public account fields in MAUI SecureStorage. Preferences contains only the API URL and restoration-block flag. Passwords and server database credentials never belong in client persistence. A restored token must pass `/api/auth/me` before the online workspace opens. See README for endpoint/certificate setup and native storage acceptance.

The historical Phase 9 run passed 163 tests with no skips, including 19 real PostgreSQL facts. The added client integration fact uses real API/JWT/database behavior with in-memory HTTP transport and a fake platform token store; actual native HTTPS and SecureStorage are separate checks. The isolated cluster was stopped after cleanup; the existing PostgreSQL Windows service was left running.

Phase 10 introduces the separate synced cache described above; Phase 9's direct online workspace remains available. The test suite now includes PostgreSQL idempotency/concurrency, REST journal participation, validation/isolation, paged cursors, tombstones/resurrection, rollback, pre-sync owned-task bootstrap, and two real SQLite replicas converging through the authenticated API. Final counts are in DEVELOPMENT_PROGRESS.md.

## Reminder storage (Phase 11)

`reminders.db3` uses its own schema version 1, with a single ReminderSettings row containing local reminder records and the next native numeric ID. Each scope/task key retains its stable ID, UTC fire instant, original wall time/zone, captured due date, caption, enabled/attempted state, and diagnostic status. Device and account scopes cannot share native IDs by accident; ID allocation/replacement and state writes use SQLite transactions. Newer/corrupt state is rejected without reset.

Desired choices and cancellations persist before native side effects. A failed notification call does not roll back or misreport an already committed task/sync write. Windows saves its one-attempt marker before showing an alert, so an uncertain delivery/crash is not automatically retried. This is best effort, not a distributed transaction with the OS.

Task/reminder captions are unencrypted local data; tokens remain in SecureStorage. Reminder settings are not server entities, EF migrations, outbox operations, or automatic cross-device subscriptions. Android keeps only nonsecret active-scope metadata in Preferences for its receiver. No Phase 11 EF update is needed; current API users need all four migrations including Phase 12B AddTaskTags.

## Saved-view metadata (Phase 12A)

`views.db3` now initializes its independent schema version 2 with SavedTaskViews (ID, scope, name, invariant-uppercase name key, local revision, JSON rule definition) and unique scope/name key. Device definitions use the device scope; cached-account definitions use canonical API origin plus account ID. Parameterized SQL and one SQLite transaction cover duplicate/quota/revision checks and each write. Definitions are capped at 50 per scope; stale updates/deletes and corrupt/newer metadata fail without a reset.

The stored JSON contains validated existing-field query rules, not task snapshots or tokens. Names/search/category rules are ordinary unencrypted local metadata and are not automatically synchronized. Deleting a definition never deletes tasks, categories, queued operations, conflicts, or reminders. Phase 12A left task/sync/reminder schemas and server payloads unchanged. Phase 12B adds the tag upgrades below; the view definitions themselves still remain local metadata. See SAVED_VIEWS.md for scope/date semantics and manual acceptance.

## Tag storage and upgrades (Phase 12B)

The device schema-2 upgrade ALTERs TaskItems with required Tags TEXT DEFAULT '[]' inside initialization's transaction. Reads restore a validated immutable TagSet; writes serialize it with the task's other fields in the same transaction. AddTaskTags applies the equivalent required Tasks.Tags text column/default in PostgreSQL through EF JSON conversion/value comparison. No global Tags/TaskTags tables or global tag rename policy is introduced for this bounded per-task value model.

sync.db3 and views.db3 advance from version 1 to 2 for the extended task/definition formats. Read-time upgrades do not rewrite legacy pending requests or saved-definition JSON. Old operations with null/absent Tags continue omitting that property for receipt matching; explicit empty sets mean clear. Old client binaries reject newer local versions. No reset, cache deletion, receipt pruning, or automatic server migration runs. No new package is needed. See TAGS.md for coordinated upgrade commands and rollback limits; dropping the server Tags column would lose tag data.
