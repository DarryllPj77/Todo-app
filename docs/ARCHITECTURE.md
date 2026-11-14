# Architecture

## Server side

ASP.NET Core hosts HTTP requests. Program.cs registers controllers/Problem Details and string priority serialization. DatabaseConfiguration registers a scoped TodoDbContext using Npgsql and a scoped ITaskService implemented by PostgresTaskService. Exception handling, HTTPS redirection, and controller routing remain separate. HealthController provides GET /api/health; TasksController implements the five task routes.

REST uses resource URLs and HTTP verbs: GET reads, POST creates, PUT updates, DELETE removes. Implemented routes are:

```text
GET    /api/tasks
GET    /api/tasks/{id}
POST   /api/tasks
PUT    /api/tasks/{id}
DELETE /api/tasks/{id}
```

TasksController delegates to the server service and awaits Task-returning operations with request cancellation. POST returns CreatedAtAction (201 with a working Location); GET/PUT return 200, DELETE returns 204, missing IDs return 404, and invalid requests/filters return 400. Guid route constraints mean malformed IDs do not match a task route. No category endpoints or PATCH route is introduced.

TaskWriteRequest is a dedicated input DTO validated with IValidatableObject and the same trimmed domain length limits. ApiController converts invalid model state into ValidationProblemDetails before an action runs. JsonUnmappedMemberHandling.Disallow rejects unknown/server-owned properties instead of accepting untrusted fields. TaskResponse maps each entity into an immutable flat scalar response with categoryId/categoryName; no navigation objects or task collections are serialized. Priority uses named JSON strings only, and dates use DateOnly's ISO JSON representation.

POST/PUT use the same full detail contract, including IsCompleted. Omitted optional values clear their fields, omitted priority selects Normal, and omitted completion selects false. This explicit PUT replacement behavior must be preserved when adding a database or clients. IDs, creation/update timestamps, and category IDs cannot be supplied in the write DTO. CategoryName assigns/reuses a case-insensitive server category; this API does not rename categories globally.

PostgresTaskService uses real asynchronous EF/Npgsql queries and transactions, with request cancellation passed through. Read-only queries use AsNoTracking and load Category for the flat response DTO. Create/update save category and task changes in a transaction; failure rolls both back. Delete uses ExecuteDeleteAsync. DbContext is scoped per request and is never shared across concurrent HTTP requests. The ITaskService interface now separates persistent server behavior from the old in-memory substitute used explicitly by portable REST contract tests.

GetTasks reuses TaskQueries after materializing the small MVP dataset, preserving ordinal case-insensitive sorting and ID tie-breaking instead of silently changing behavior to a database collation. DueToday uses server-local time through TimeProvider; user/time-zone policy is future work. Data survives a new API host because it is in PostgreSQL; MAUI's SQLite remains separate. Paging/narrower SQL queries can be added when actual data size warrants them.

The host remains Development-only until deployment hardening/acceptance is complete. It checks ConnectionStrings:TodoDatabase and validates JWT options at startup; neither memory nor an implicit signing key is a normal fallback. Launch profiles set Development. Portable tests explicitly replace the task service and auth handler; PostgreSQL authentication tests use the real JWT handler. Development mode itself is not authorization for a publicly exposed service.

TodoDbContext, Fluent entity configurations, and the generated InitialCreate migration/model snapshot live in Infrastructure/Persistence/Server. API/Configuration owns host registration; API/Data remains reserved. Migrations are applied explicitly with the pinned local EF tool, not automatically during API startup. See DATABASE.md for commands and role/secret setup.

Domain remains framework-independent. TaskItem now has a scalar CategoryId so a foreign key survives loading without navigation. Private materialization constructors/setters let EF restore IDs/creation timestamps; public constructors/mutation methods retain validation. SetCategory clears even an unloaded relationship's scalar ID. Category's read-only collection maps through its private tasks field. Phase 7 preserved SQLite's restoration/schema; Phase 12B later adds tags through an explicit version-2 upgrade, with regressions passing.

The server stores uppercase-invariant category NameKey and nullable legacy OwnerId as EF shadow properties. A unique (OwnerId, NameKey) index allows separate users to use the same name. Parameterized INSERT ... ON CONFLICT DO NOTHING plus an owner-scoped query creates/reuses a category atomically within the authenticated account. Existing casing is retained; category rename would need to update NameKey. Concurrent task edits remain last-writer behavior; sync revisions/conflicts are future work.

### Authentication and ownership (Phase 8)

AuthController exposes anonymous register/login and authorized me. AuthService stores UserAccount records using ASP.NET Core PasswordHasher, not custom password cryptography. NormalizedEmail is unique. Registration accepts valid email and a 12–128 character password without trimming it; login verifies its hash, uses a generic failure for unknown/wrong credentials, and rehashes when the framework requests it. Unknown-account login still performs password verification. A process-local per-IP fixed-window limiter covers both auth endpoints (10 requests/minute, 429 on excess); this is not distributed/account lockout protection.

JwtTokenService issues HS256 access tokens with sub, jti, iat, issuer/audience, and a default 15-minute expiry. The signing key is at least 32 random bytes encoded in Base64, supplied only through configuration. JwtBearer validates the allowed algorithm, signature, issuer, audience, expiry/not-before with zero clock skew, and confirms the account still exists. TasksController uses Authorize. HttpCurrentUser reads the validated sub claim; PostgresTaskService applies that owner to every query/write and never accepts owner IDs from DTOs. Cross-user IDs return 404 rather than revealing another user's resource.

The AddAuthenticationOwnership migration creates Users and nullable OwnerId FKs on Tasks/Categories, preserving pre-authentication rows as inaccessible unowned data. New API writes always set a validated user's owner ID, and category lookups/upserts are owner-scoped. No new account automatically acquires legacy data. User deletion is restricted while owned rows exist; there is no public account-deletion endpoint. An administrator must explicitly review any legacy ownership assignment.

This is a learning/development credential issuer, with short-lived access tokens and re-login after expiration. No refresh tokens, email verification, password reset, per-token revocation, or production identity-provider integration exists. Removing a client token is local logout, not immediate invalidation of an already issued JWT. Configure/review those production requirements before changing the Development-only guard. See [Microsoft's JWT validation guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0).

PostgreSQL timestamp precision is microseconds. The context normalizes UTC task timestamps before SaveChangesAsync, preserving exact POST/PUT-to-GET round trips. No-tracking DTO reads, constraints, FK delete behavior, rollback, repeatable migrations, and data retained across new hosts are verified against PostgreSQL 18 in isolated test databases. The provider and EF package versions are pinned and compatible; details are in DATABASE.md.

## Client side

MAUI provides native Windows, Android, iOS, and macOS (Mac Catalyst) applications from shared C# and XAML. XAML describes presentation. MVVM separates a View from the presentation state/commands in its ViewModel and the data/services it uses.

The current flow is:

```text
MauiProgram: register storage, navigation, pages, and ViewModels
  -> App.CreateWindow: create a DI scope for each window
     -> NavigationPage -> TaskListPage -> TaskListViewModel -> SQLite
          -> TaskEditorPage -> TaskEditorViewModel -> SQLite
          -> AccountPage -> AccountViewModel / ServerWorkspaceViewModel
               -> TodoApiClient -> HTTPS API -> PostgreSQL
               -> ClientSession -> ISecureTokenStore -> MAUI SecureStorage
```

ObservableViewModel implements INotifyPropertyChanged using a readable SetProperty helper. TaskListViewModel exposes ObservableCollection<TaskRow>, selection/filter state, busy/error messages, and commands. TaskEditorViewModel holds separate editable fields so typing or canceling does not mutate stored entities. TaskRow is a read-only display snapshot with a title and status/date/category summary. Refresh rebuilds the rows after writes.

AsyncCommand implements ICommand, prevents overlapping execution, updates CanExecute, and catches exceptions at the async event boundary. ViewModels handle validation and operation errors without clearing edits on a failed save. A successful create is marked saved before returning to the list, preventing a navigation failure from creating duplicates on retry. Delete requests require confirmation; a missing task is reported and the list is refreshed. Busy state disables conflicting controls. Validation errors explain title/description/category limits and invalid priorities.

Views contain bindings and presentation events only: appearing refreshes the list, and a picker change requests its refresh command. TaskNavigationService implements a small ITaskNavigation boundary for pushing/popping editor pages and showing native delete confirmation. Tests replace that boundary without starting a device. No database logic lives in code-behind.

Each window owns a DI scope for its navigation service and list ViewModel/page; the scope is disposed when its Window is destroyed. Editor pages/ViewModels are transient within that scope. The SQLite service is shared as a singleton and opens an independent connection/transaction per call. This prevents one window's navigation from targeting another window.

NavigationPage provides normal list/editor navigation on both desktop and phone layouts. Maximum content widths and a scrollable editor keep the first UI modest; final desktop `Categories | Tasks | Task Details` panes remain future work. AppThemeBinding styles support both system themes. The native Windows dark empty state and editor have been inspected; light/narrow/mobile acceptance checks remain pending. The .NET 10 nullable DatePicker binds to DateTime? and converts to DateOnly? on save; Clear restores null.

### Account and online workspace (Phase 9)

TodoApiClient uses client-owned DTOs; MAUI does not reference the API project. A long-lived HttpClient has a 15-second timeout, normal TLS validation, and automatic redirects disabled. Each request captures its own origin/token and places Authorization on that request, never on shared default headers. HTTPS base URLs must be root URLs without credentials, paths, queries, or fragments. Credentials are sent only to the captured origin; no automatic retries are performed.

Singleton ClientSession owns the shared session across windows. SecureTokenStore persists a protected JSON envelope containing origin, bearer token, account, and expiry, with no password/plaintext fallback. ClientSettings stores only the configured URL and restoration block in Preferences. Restoration rejects malformed, expired, or mismatched-origin envelopes and requires `/me` before protected workspace use. Expiry is checked on each protected operation, not by a background timer. A current-session 401 clears the session; 403 retains it. Offline verification preserves the unverified session for an explicit retry.

A monotonic session version and serialized secure-storage operations prevent an old login/restore/response from overwriting a newer account or undoing logout. Logout clears memory immediately and persists a restoration block before removing the protected envelope. Failed removal cannot silently restore the account next launch; the UI offers retry. If settings and secure storage both fail, the error asks the user to keep the app open and retry. Endpoint changes sign out before saving the new origin.

AccountViewModel and ServerWorkspaceViewModel are scoped per window and dispose session subscriptions. Session-version changes clear all server rows, selections, and drafts across windows; in-flight responses from earlier versions are discarded. Reverification alone does not discard drafts. Password drafts are cleared after every operation/attempt and session change. Friendly errors expose neither raw server payloads nor tokens.

ServerWorkspaceViewModel handles online CRUD/filtering with separate editable drafts and native delete confirmation. After an uncertain write or a failed post-write refresh, further writes require an explicit successful refresh. An acknowledged create clears its draft before refreshing to prevent duplicate creation. No server operation reads/writes SQLite, queues an upload, or imports device tasks. Local task navigation/CRUD remains available without an account or network.

The portable tests link these actual services/ViewModels and substitute only platform storage/settings/navigation and HTTP transport. A real JWT/PostgreSQL integration test runs the client through registration, CRUD, restored-session verification, and logout. This in-memory HTTP transport does not validate native socket TLS or platform SecureStorage; those remain manual acceptance checks.

## Project references and responsibility

```text
MAUI -------------> Domain
  |                 ^
  +-> Infrastructure+
API --------------> Domain
  +-> Infrastructure
Tests ------------> API, Domain, Infrastructure
```

Domain contains TaskItem, Category, TaskPriority, TaskFilter, the ILocalTaskService/TaskDetails contract, and shared TaskQueries rules without package dependencies. Infrastructure references Domain and contains SQLite services plus server EF mappings/migrations. Neither library references MAUI or ASP.NET Core. MAUI registers local SQLite persistence; API registers its PostgreSQL context/service. They use separate databases. Host-specific infrastructure assemblies can be split later if platform/deployment requirements justify it.

Dependency injection lets the framework create registered objects and supply constructor dependencies. MAUI supplies task ViewModels/pages, per-window navigation, and singleton ReminderAwareTaskService over the concrete SqliteLocalTaskService at FileSystem.AppDataDirectory/todoapp.db3. ASP.NET Core independently registers controllers through AddControllers. No second repository interface is needed.

## Intended algorithm and main features

The current task logic lives in plain domain methods. Construction validates and trims title/description, validates the priority enum, generates an ID, and captures UTC timestamps. UpdateDetails validates all inputs before assigning any fields, so rejected edits leave the task unchanged. Equivalent edits and repeated SetCompleted/SetCategory requests are no-ops. Real edits update UpdatedAt while preserving identity and CreatedAt.

TaskItem.SetCategory updates the scalar CategoryId and both loaded navigation sides. Category exposes a read-only task collection. SQLite rebuilds references through internal Restore factories, while EF uses private materialization support and Fluent mappings. Domain grants Infrastructure internal restore access; public identity setters are not exposed. No category deletion service exists yet. Category rename persistence is outside the task-service contract and would need to update the server NameKey as well.

Past due dates remain valid so overdue tasks can be represented. DueDate is DateOnly? because a deadline day is different from a notification time. Priority has explicit stable values: Low = 0, Normal = 1, High = 2. Domain methods use exceptions for invalid programmer/service input; future ViewModels and API validation will translate failures into useful messages. The domain has no UI change-notification or service dependencies.

## Local task service (Phase 3)

```text
Caller/test -> ILocalTaskService (Domain contract)
                -> InMemoryLocalTaskService (Infrastructure)
                     -> Dictionary<Guid, TaskItem> + domain mutation methods
```

CreateAsync accepts TaskDetails and returns the created entity. GetByIdAsync returns null for a missing ID. UpdateAsync, SetCompletedAsync, and DeleteAsync return false for a missing task and true for an existing task, even if its state already matches. TaskDetails is a full replacement of editable fields, not a partial patch: omitted optional values clear them, and priority defaults to Normal. Completion is changed separately. Invalid fields throw domain exceptions; cancellation is checked before each operation and before any mutation. Category CRUD is not part of this phase.

UpdateAsync delegates validation/mutation to TaskItem before changing category, so a rejected edit leaves task details and category links untouched. DeleteAsync removes the task from both the store and its category collection. No database transactions or synchronization tombstones are implemented yet.

GetTasksAsync applies these LINQ predicates, then sorts titles case-insensitively using ordinal comparison, with ID as a deterministic tie-breaker:

| Filter | Selection |
| --- | --- |
| All | Every stored task |
| Pending | IsCompleted is false |
| Completed | IsCompleted is true |
| DueToday | DueDate equals the current local calendar date; completed tasks are included |
| HighPriority | Priority equals High; completed tasks are included |

Undated tasks do not match DueToday; yesterday/tomorrow are excluded. TimeProvider.System supplies local time by default. Tests supply a small TimeProvider subclass to exercise UTC+8/UTC-8 and advancing past midnight without waiting or depending on the test machine's time zone. Today is recomputed on each query.

The contract returns Task<T>. The in-memory version has no I/O: Task.FromResult supplies completed results without fake delays. Its results have read-only, materialized membership but live TaskItem references. Treat all service outputs as read models and use service methods for writes; UI editor fields should be separate. The temporary in-memory service is sequential-use and not thread-safe.

## SQLite persistence (Phase 4)

```text
MAUI DI -> ILocalTaskService -> SqliteLocalTaskService
                                  -> SqliteTaskDatabase (schema/version)
                                  -> parameterized SQL -> app-data/todoapp.db3
```

Microsoft.Data.Sqlite 10.0.15 bundles native SQLite. The Windows MAUI app builds with the shared infrastructure changes; other native platforms remain unverified. SQLite still uses explicit SQL/versioning while the server now uses EF Core/PostgreSQL. Both implementations remain in Infrastructure for this small solution; EF Design tooling is private to the API project. Separate client/server infrastructure assemblies can be introduced if mobile packaging or deployment requirements justify that split.

SQLite's provider executes its async ADO.NET methods synchronously ([official limitation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)). This implementation uses Task.Run once per short database operation to keep blocking work off the future UI thread. It opens/disposes a connection and transaction inside that worker and never shares them between threads. This is a deliberate SQLite adapter choice, not guidance to wrap future HTTP/PostgreSQL async I/O in Task.Run.

Each call initializes/checks the schema version, then uses a transaction for its operation. Category insertion and task insertion/update commit together. Disposing an uncommitted transaction rolls back changes on validation, SQL errors, or cancellation observed before commit. Failures propagate to callers. Tokens are checked before opening, while loading rows, and immediately before commits; a running native statement or lock wait cannot be instantly canceled. The connection has a five-second busy timeout. Cancellation arriving after commit cannot undo a successful write.

Reads load categories and tasks into a fresh graph, restoring IDs, UTC timestamps, and shared category references. Returned entities are detached snapshots: later writes do not update them, and editing them does not save data. Refresh after a write. Create returns only the created task and its category reference; use a read query for a complete category/task graph. No live object cache is maintained.

For this small dataset, reads and task edits load the current graph, and both stores use TaskQueries for identical LINQ filter/order behavior. Transactions serialize read-modify-write operations, avoiding lost writes from separate service instances; this is not user-visible conflict handling or distributed sync. SQL-level paging and narrower queries can be introduced if real data volume warrants them.

Assigning a category saves its ID/name if new. Reusing an existing ID uses the stored name instead of overwriting it from a stale caller object. Inputs are not attached or mutated, including on failed writes. Category rename/delete operations remain outside ILocalTaskService. Task deletion retains the category row; SQLite's category FK uses ON DELETE SET NULL for any future category deletion operation.

The local MVP is create/view/edit/delete, completed/uncompleted, title/description, optional due date/category, priority, five basic filters, SQLite persistence, MVVM, DI, and light/dark themes. Server authentication and the separate online client workspace are implemented; the separate synced account workspace now reconciles offline changes. Device-local reminders are implemented in Phase 11; advanced task relationships remain later work.

## Offline account synchronization (Phase 10)

The main local task flow stays on `todoapp.db3`. The synced toolbar route is `SyncPage -> SyncWorkspaceViewModel -> SqliteSyncStore (sync.db3)` for cached reads/writes and `SyncEngine -> TodoApiClient -> SyncController/SyncService -> PostgreSQL` for reconciliation. Account & server retains its direct online workspace. No device-only task is automatically imported or assigned.

Shared `Domain/Sync` records define the small wire protocol without adding another project/package. SqliteSyncStore is an Infrastructure adapter with transactional account/origin partition state: local intent, immutable pending operation, latest server entry, conflicts, retry time, and download cursor. Acknowledgements preserve later local edits; stale receipt responses cannot regress newer downloads or silently rebase over another device's work. Separate store instances serialize SQLite transactions, and stale editors are rejected instead of overwriting newer cache rows.

The API's SyncJournal takes a transaction-scoped per-owner PostgreSQL advisory lock before task access. Normal PostgresTaskService creates/updates/deletes participate in the same journal. SyncRecords retains each task's latest revision or tombstone; SyncReceipts makes identical operation replay return the original result. Task write, revision, and receipt commit together. Lazy initialization records existing owned tasks without claiming legacy anonymous rows. Client-generated IDs are accepted only by the validated sync protocol; normal REST create semantics are unchanged.

BaseRevision implements optimistic concurrency. A mismatch preserves local and server versions for explicit Keep mine / Use server resolution. Downloads return latest states in revision order, at most 100 per page; SQLite commits each page and its cursor together. No wall-clock conflict ordering is used. Records/receipts remain retained; direct SQL writes and database restores require reviewed reconciliation.

The singleton engine prevents overlapping sync across windows and captures account/origin/version for each pass. It uploads up to 100 queued operations and downloads up to 100 pages, retains failed operations, and persists exponential retry backoff. The page requests sync on demand, opening, connectivity hints, and a 30-second visible-page timer. It stops scheduling when hidden; it is not an OS background service. Native connectivity is a hint rather than proof of API access. Unsaved drafts are preserved during automatic sync, and a stale editor must be reviewed before saving.

Tokens stay in SecureStorage; cached task content is unencrypted SQLite. Sign-out/account switches clear visible rows/drafts without deleting another account's queue. Selecting a cache needs a current saved session; expired session re-entry requires reauthentication. See [SYNCHRONIZATION.md](SYNCHRONIZATION.md) for protocol details, limitations, recovery boundaries, and manual native acceptance.

## Device-local reminders (Phase 11)

The API and sync contracts do not change. Reminder time is not inferred from DateOnly DueDate or copied from another device. Domain/Reminders defines ReminderTask, ReminderRecord, and a deterministic local-time-to-UTC rule that rejects invalid/ambiguous DST choices and requires a time one minute to 365 days ahead.

`RemindersPage -> RemindersViewModel -> ReminderService -> SqliteReminderStore (reminders.db3)` persists explicit per-device choices. ReminderTaskCatalog reads device SQLite and the active account's cached rows. The service uses IReminderPlatform for permission checks, scheduling, display, and removal; no native APIs live in the ViewModel. Due-date changes/completion/deletion disable reminders for review, title edits retain the instant, and account changes suspend inactive scopes. Payloads contain generic text and a numeric identifier, without task/account details.

ReminderAwareTaskService decorates ILocalTaskService and reconciles after successful writes, while the catalog reads the concrete service to avoid recursion. SqliteSyncStore's exception-isolated Written callback runs after commit, allowing reconciliation of cached writes/downloads/conflicts without reporting committed writes as failures. ReminderRuntime reconciles on window creation/resume, session changes, and a 15-second app timer; native failure status remains visible and retryable.

Windows uses AppNotificationManager with running-app evaluation rather than claiming closed-app scheduling for the unpackaged build. Android uses inexact alarms, explicit permission/channel checks, and receivers that revalidate persisted choices/task state and restore future alarms after boot. Apple uses local notification requests and a retained foreground delegate. Only Windows source/XAML is compiled here; actual OS delivery and mobile builds remain manual acceptance gates. See [NOTIFICATIONS.md](NOTIFICATIONS.md) for semantics, limitations, failure ordering, and test steps.

## Custom filters and saved views (Phase 12A)

Phase 12A kept the server/sync contracts unchanged; Phase 12B adds tags to them. Domain/Views defines immutable filter/candidate/saved-definition records plus pure normalization and LINQ query rules. Search is literal and ordinal case-insensitive across title/description/category; combined filters use AND. Relative dates use the device day at load time. Ordering is deterministic, with task-ID tie-breakers and undated-last due ordering.

`TaskViewsPage -> TaskViewsViewModel -> SqliteTaskViewStore (views.db3)` manages named definitions. Results read ILocalTaskService or only the active API/account's SqliteSyncStore partition. Applying a view projects existing snapshots; it never writes task, outbox, conflict, or reminder state. Device rows navigate to the existing editor; cached rows explicitly open the Synced tasks workspace. Global queue/conflict counts remain visible outside result filters.

Editable filter controls and applied rules are separate. Selecting a definition fills the draft; Apply or an explicit successful save changes applied results. Source generations/session versions reject stale asynchronous results, and account changes clear account names/drafts/rows. Device-only views remain independent of server login. Saved-definition revisions reject stale edits/deletes from other windows; deletion rechecks the scope after native confirmation and leaves tasks unchanged.

The independent views.db3 version-1 schema stores ID/scope/name key/revision/JSON definition. Transactions protect duplicate names, a 50-view scope quota, revision checks, and writes. Metadata stays local and unencrypted; no package, server migration, or existing task schema changes. See [SAVED_VIEWS.md](SAVED_VIEWS.md). Tags are added by Phase 12B below; the remaining Phase 12 model/layout work stays pending.

## Task tags (Phase 12B)

TagSet is an immutable, normalized, value-equal collection of at most ten labels (1-32 characters each). It has an explicit JSON array converter and is independent of EF/MAUI. TaskItem stores a non-null set, updates it atomically with editable fields, and preserves tags when legacy callers omit them. Explicit empty sets clear them. Categories remain a separate single assignment; tag labels are per-task values rather than a global catalog/entity graph.

SQLite stores tags as JSON text in a new TaskItems.Tags column through its atomic 1-to-2 schema upgrade. PostgreSQL's AddTaskTags migration adds Tasks.Tags with [] default, explicit EF JSON conversion, and immutable value comparison. Normal API/sync writes journal complete tag sets with their existing owner/revision rules. No tag IDs or owner IDs are accepted from callers, and invalid tag arrays fail before mutation.

Phase 12B introduced protocol 2 for tags; hierarchy introduced 3 and dependencies now use 4. The engine probes capability before feature-aware upload and blocks old APIs while retaining the same operation. Null tags/Parent/Prerequisites are omitted from legacy serialized SyncTaskData so old receipt request bytes remain stable. TagSet's deep equality prevents deserialized collections from causing endless acknowledgement/requeue cycles. Tags follow whole-task conflicts, without automatic set unions. The tag upgrade introduced schema 2 for sync/views; hierarchy advanced sync to 3, and dependencies now advance device/sync to 4. Coordinated client/API upgrades are required.

All editors accept comma-separated labels and explicitly clear with an empty set. Saved-view search includes tag substrings; exact AllListed/AnyListed/Untagged rules combine with existing predicates. Notifications remain generic and tag-only edits do not cancel a reminder. See [TAGS.md](TAGS.md) for storage tradeoffs, compatibility, migration ordering, and native acceptance.

## Parent hierarchy (Phase 12C.1)

Domain/Relationships defines TaskParent (an optional change wrapper), TaskLink, and shared pure cycle/depth/delete validation. TaskItem stores scalar ParentTaskId; services validate the workspace graph transactionally. SetParentAsync changes only placement. The server retains its per-owner advisory lock through validation/write/journal, preventing opposite concurrent moves from creating a cycle. Self FKs/checks/indexes add storage guards but do not replace ownership/cycle/depth checks.

Device schema 3 and AddTaskHierarchy preserve existing rows as roots. Sync schema 3 retains immutable old pending operations; protocol 3 guards new parent intent. The engine orders parent creation/child links and known child detach/delete/parent deletion. Structural rejection becomes a receipted whole-task conflict with a safe reason, preserving local intent. ReparentAsync repairs against expected local fields and remote revision after explicit confirmation; normal edits never replace an in-flight payload.

HierarchyViewModel reads one selected device/account scope, defensively flattens trees, excludes descendant parent choices, and clears account results when session context changes. The native Task hierarchy page and parent-only service keep ordinary editors simple and preserve unrelated fields. Parent completion does not cascade; parent deletion is restricted. Saved views remain flat, reminders remain independent, and blocking dependencies are deferred to 12C.2. See [HIERARCHY.md](HIERARCHY.md).

## Build boundaries

`TodoApp.slnx` includes every project. `TodoApp.Core.slnf` selects the four SDK-only projects from that same solution for machines without mobile workloads. It is an explicit partial validation path. A passing core build cannot validate XAML, platform manifests, device deployment, or themes. See development progress for actual results.

The tests link the actual portable ViewModel/model/interface source files from the MAUI project, rather than maintaining test copies. Those classes use only .NET APIs; native pages/navigation are compiled by the Windows MAUI build and checked separately. Sixteen presentation cases test editing, validation, retry, cancellation of edits, confirmation, missing tasks, busy/double-save behavior, filtering, and navigation failures.

This checkout has an ignored portable SDK/workload under .tools/dotnet because the machine-wide install was canceled during elevation. scripts/Run-Windows.ps1 selects it when present. TodoAppTargetFrameworks limits only the client; overriding the generic TargetFrameworks property retargeted referenced libraries and caused NETSDK1005. The Windows build bundles Windows App SDK files using WindowsAppSDKSelfContained for unpackaged development launch ([deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)). A fresh clone still needs an SDK/platform workload installation; local tools are not committed.

## Task dependencies (Phase 12C.2)

Domain/Relationships adds immutable TaskPrerequisites, a strict GUID-array converter, DependencyNode, and iterative graph validation, separately from parent hierarchy. The bounded outgoing set is stored as JSON text with EF conversion/value comparison. There are no per-edge revisions or JSON-member FKs; service transactions/per-owner locks enforce membership, ownership, cycles, completion transitions, and incoming-delete restriction. Direct administrative SQL must preserve these rules. Existing parent/category FKs remain.

Device/sync schema 4 and AddTaskDependencies retain existing rows/queues; legacy null prerequisites stay omitted for receipt replay. Protocol 4 gates explicit prerequisite input and every deletion, which needs server incoming-link validation. Upload eligibility waits for queued prerequisite existence/completion and incoming-link removal. Structural conflicts retain local fields and a safe reason; DependenciesViewModel supports scoped staged links, readiness summaries, expected-local/revision repair after confirmation, Use server, and account clearing. Ordinary editors preserve links. Reopening does not cascade completed dependents. Views remain flat schema 2; reminders remain schema 1. See DEPENDENCIES.md.

## Urgency and importance (Phase 12D)

EisenhowerQuadrant represents four explicit Boolean combinations plus Unclassified; helper methods return unknown axes for Unclassified. It is independent of priority/dates/completion/relationships. Strict shared string-enum JSON and Domain/SQL validation protect stable stored values. AddTaskPlanning/device schema 5 add checked integer Quadrant default 0, preserving existing rows/times. Planning-only service/API operations retain unrelated fields.

Protocol 5 gates explicit nullable Quadrant writes while omitted input preserves classification and explicit Unclassified clears. Legacy null fields stay absent for receipt replay. sync schema 5 retains immutable operations; planning conflict repair uses confirmation and expected-local/remote revision guards. PlanningViewModel reads one device/account scope, shows quadrant counts/filters, and clears old account rows/drafts. Saved-view nullable predicates use schema 3 and AND composition; old definitions default Any. Reminder schema remains 1. See EISENHOWER.md.

## Completion-based recurrence (Phase 12E)

Domain/Recurrence defines immutable RepeatRule/RepeatState, strict named frequency JSON, anchored DateOnly arithmetic, monotonic counter/configure/advance validation, and semantic exceptions. A task stays pending at one current date. Daily/weekly/monthly rules advance the same ID after explicit confirmed completion; no timed job/template expansion or history table is introduced. Incoming dependency links forbid active repeating prerequisites, and own prerequisites gate each advance. Parent/other task fields remain independent.

AddTaskRecurrence/device schema 6 preserve old tasks as non-repeating. Server SyncRecords retains bounded RecurrenceFloor through tombstones/restoration; task/date/counter/journal/receipt share the owner lock/transaction. Expected RepeatState makes retried/concurrent REST completion advance once, while base revisions/receipts protect sync. Protocol 6 gates explicit state; legacy null fields remain absent. The account outbox allows one unacknowledged occurrence advance, preserving frozen operations and later normal/configuration edits. Conflicts reconfigure against the latest remote counter with confirmation or Use server; old-account/stale windows are rejected.

RecurrenceViewModel exposes scoped rules/previews/confirmed completion and clears session data on account change. Date changes cancel old reminders without scheduling another automatically. sync schema 6 guards state/floor JSON; views remain 3, reminders 1. Completion-driven recurrence needs no Hangfire or timer-generated job; timed generation would require a separate reviewed scheduler design. See RECURRENCE.md.

## Focus and durable local history (Phase 12F)

Domain/Focus defines preferences, immutable session/entry snapshots, lifecycle/reason enums, monotonic-delta policy, and pure TimeSpent aggregation. SqliteFocusStore owns an independent schema-1 ledger/settings file, revisions, one-active partial unique index, and a process file lease. FocusTaskCatalog reads only pending device/current-account cached tasks, with account-expiry guards. No task mutation, API endpoint, server migration, or sync contract is introduced.

Singleton FocusService serializes all windows' start/control/heartbeat/recovery operations, tracks monotonic elapsed time, saves five-second checkpoints, and uses scope/session IDs plus SQL revisions to reject stale controls. File ownership excludes another app process. Runtime heartbeats continue independently of page navigation/desktop deactivation; all-window stopped/destroyed events interrupt, and resume never restarts a session. Task/occurrence/account changes, long timing gaps, storage failures, or restart end sessions conservatively without inventing duration. FocusRuntime provides the process heartbeat/lifecycle adapter.

FocusViewModel/page show scoped pending tasks, countdown, configurable/manual focus/break controls, ledger-derived task/overall TimeSpent, captured-title history, and long-break suggestions. UI polling preserves unsaved duration drafts and updates rows without resetting the collection on every tick. Account changes clear private data; another scope's active session is represented by a generic busy flag. Focus history stays local in focus.db3; other file/protocol versions remain unchanged. See FOCUS.md.

## Adaptive workspace (Phase 12G)

TaskListViewModel retains the existing device service/navigation contracts. It materializes immutable TaskRow snapshots, derives typed WorkspaceCategory groups from the service-filtered read, and applies literal category/search narrowing in memory. Read-only selection is retained by task ID on refresh; nonmatching/deleted rows clear selection and action commands. Layout state has no database writes or dependency on platform names.

TaskListPage switches at 1180 logical pixels of content-grid width between three desktop panes and compact Tasks/Details navigation. A Tools action sheet routes to the existing feature/account pages, preserving their independent scopes and write boundaries. Editors remain navigation pages so resizing the root cannot discard a draft. Synced-account rows are not merged into the device workspace. See ADAPTIVE_WORKSPACE.md for behavior and native acceptance.

An opt-in Debug preview redirects all SQLite paths to an explicit directory, seeds synthetic device rows only into an empty task store, replaces settings/token/native-notification adapters, rejects HTTP calls, and bypasses lifecycle runtimes. Release ignores the preview variable. Normal runtime paths and registrations retain their existing behavior.

## Calendar planning and file export (Phase 13A)

Domain/Calendar defines fixed-UTC time blocks and an RFC 5545 exporter. SqliteCalendarStore owns a separate schema-1 calendar.db3, scoped snapshots, transactional overlap checks and local revision concurrency. CalendarViewModel reads device tasks or the current account cache without mutating tasks/outbox data. Existing blocks retain task/title/zone identity; new blocks require a live pending nonconflicted source task. Scope/version/expiry guards reject stale asynchronous results and export handoffs.

CalendarPage is reached through Tools > Calendar & time blocks. Draft-aware refresh/new actions, local CRUD and selected-block export remain separate from provider authorization. CalendarFileExporter writes a UTF-8 file in a restricted cache export directory and opens the native share UI; it does not contact Google or report import success. Android FileProvider paths are restricted to that directory; native/mobile validation remains pending. Google is the chosen first provider for later OAuth/event-link/reconciliation increments. See CALENDAR.md.

## Google consent and protected grants (Phase 13B Windows)

GoogleOAuthProtocol handles fixed Google authorization/token/UserInfo/revoke endpoints, PKCE/state, grant validation, subject verification and refresh rotation. GoogleLoopbackBroker binds only IPv4 loopback on an OS-selected port, validates bounded callback HTTP/path/Host/query state, and releases the listener on cancellation. The native browser adapter opens the system browser; unsupported/preview adapters cannot authorize. The provider HTTP client is separate from Todo API auth and disables redirects, bounds response buffering and has a 15-second timeout.

Singleton GoogleConnectionService serializes windows, captures the Todo account/origin/version before waiting for its gate, and rejects canceled/stale results. GoogleTokenVault stores version-1 grants only in SecureStorage using hashed partition keys, with a non-secret Preferences restoration block marker and in-process fail-closed guard. Saved grants bind Desktop client and verified Google subject; raw provider/token objects never enter ViewModel labels. GoogleConnectionViewModel/page exposes explicit Connect/Check/Cancel/local disconnect/confirmed revocation through Tools. No startup authorization, event write, task mutation, SQLite migration or provider-event mapping is added. Windows flow and mobile limits/setup/acceptance are documented in GOOGLE_CALENDAR.md.
