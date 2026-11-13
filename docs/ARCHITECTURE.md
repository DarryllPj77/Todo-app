# Architecture

## Server side

ASP.NET Core hosts HTTP requests. `Program.cs` is the composition root: it registers controller services and Problem Details, then assembles exception handling, HTTPS redirection, and controller routing. `HealthController` provides only `GET /api/health`.

Later, task controllers will translate REST requests into application operations. REST uses resource URLs and HTTP verbs: GET reads, POST creates, PUT updates, DELETE removes. The intended routes are:

```text
GET    /api/tasks
GET    /api/tasks/{id}
POST   /api/tasks
PUT    /api/tasks/{id}
DELETE /api/tasks/{id}
```

These routes are a plan, not implemented endpoints. DTOs will define input/output contracts so EF navigation graphs and server-managed fields are not exposed for unrestricted writes. Plan for validation responses (400), missing resources (404), creation with a location (201), and successful deletion (204). Authentication and user ownership must precede public multi-user deployment.

EF Core will map domain objects to PostgreSQL tables. Its DbContext and model configuration belong in Infrastructure/Persistence, outside controllers. API/Configuration will own host-side registrations; API/Data is reserved for server-only startup/seed helpers if needed, not a second DbContext. Database migrations will be generated when persistence is introduced, not hand-invented now.

## Client side

MAUI provides native Windows, Android, iOS, and macOS (Mac Catalyst) applications from shared C# and XAML. XAML describes presentation. MVVM separates a View from the presentation state/commands in its ViewModel and the data/services it uses.

The current flow is:

```text
MauiProgram: register storage, navigation, pages, and ViewModels
  -> App.CreateWindow: create a DI scope for each window
     -> NavigationPage -> TaskListPage -> TaskListViewModel -> SQLite
          -> TaskEditorPage -> TaskEditorViewModel -> SQLite
```

ObservableViewModel implements INotifyPropertyChanged using a readable SetProperty helper. TaskListViewModel exposes ObservableCollection<TaskRow>, selection/filter state, busy/error messages, and commands. TaskEditorViewModel holds separate editable fields so typing or canceling does not mutate stored entities. TaskRow is a read-only display snapshot with a title and status/date/category summary. Refresh rebuilds the rows after writes.

AsyncCommand implements ICommand, prevents overlapping execution, updates CanExecute, and catches exceptions at the async event boundary. ViewModels handle validation and operation errors without clearing edits on a failed save. A successful create is marked saved before returning to the list, preventing a navigation failure from creating duplicates on retry. Delete requests require confirmation; a missing task is reported and the list is refreshed. Busy state disables conflicting controls. Validation errors explain title/description/category limits and invalid priorities.

Views contain bindings and presentation events only: appearing refreshes the list, and a picker change requests its refresh command. TaskNavigationService implements a small ITaskNavigation boundary for pushing/popping editor pages and showing native delete confirmation. Tests replace that boundary without starting a device. No database logic lives in code-behind.

Each window owns a DI scope for its navigation service and list ViewModel/page; the scope is disposed when its Window is destroyed. Editor pages/ViewModels are transient within that scope. The SQLite service is shared as a singleton and opens an independent connection/transaction per call. This prevents one window's navigation from targeting another window.

NavigationPage provides normal list/editor navigation on both desktop and phone layouts. Maximum content widths and a scrollable editor keep the first UI modest; final desktop `Categories | Tasks | Task Details` panes remain future work. AppThemeBinding styles support both system themes. The native Windows dark empty state and editor have been inspected; light/narrow/mobile acceptance checks remain pending. The .NET 10 nullable DatePicker binds to DateTime? and converts to DateOnly? on save; Clear restores null.

## Project references and responsibility

```text
MAUI -------------> Domain
  |                 ^
  +-> Infrastructure+
API --------------> Domain
  +-> Infrastructure
Tests ------------> API, Domain, Infrastructure
```

Domain contains TaskItem, Category, TaskPriority, TaskFilter, the ILocalTaskService/TaskDetails contract, and shared TaskQueries rules without package dependencies. Infrastructure references Domain and implements both the SQLite service and the earlier in-memory learning service. Neither library references MAUI or ASP.NET Core. Infrastructure now depends on Microsoft.Data.Sqlite 10.0.15; only MAUI registers local persistence. The API does not use the local database. Server EF/Npgsql registration remains a future phase; split assemblies if host-specific dependencies later warrant it.

Dependency injection lets the framework create registered objects and supply constructor dependencies. MAUI supplies the task ViewModels/pages, per-window navigation, and singleton SqliteLocalTaskService using FileSystem.AppDataDirectory/todoapp.db3. ASP.NET Core independently registers controllers through AddControllers. No second repository interface is needed.

## Intended algorithm and main features

The current task logic lives in plain domain methods. Construction validates and trims title/description, validates the priority enum, generates an ID, and captures UTC timestamps. UpdateDetails validates all inputs before assigning any fields, so rejected edits leave the task unchanged. Equivalent edits and repeated SetCompleted/SetCategory requests are no-ops. Real edits update UpdatedAt while preserving identity and CreatedAt.

TaskItem.SetCategory removes the task from its previous category and adds it to the new one. Category exposes a read-only collection so callers cannot bypass that relationship logic. CategoryId derives from the category reference; SQLite stores a scalar FK and rebuilds references when reading. Internal Restore factories preserve saved identities/timestamps without public setters. Domain grants Infrastructure access through InternalsVisibleTo; neither UI nor API receives public hydration methods. No category deletion service exists yet. Category rename preserves its ID and tasks in memory; category rename persistence is not part of the task-service contract.

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

Microsoft.Data.Sqlite 10.0.15 was checked for .NET compatibility and restored successfully. It bundles native SQLite. The Windows MAUI app now builds and opens the SQLite-backed task list; other native platform packages remain unverified. EF Core remains reserved for the PostgreSQL server phase so local SQL and schema responsibilities stay visible while learning.

SQLite's provider executes its async ADO.NET methods synchronously ([official limitation](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async)). This implementation uses Task.Run once per short database operation to keep blocking work off the future UI thread. It opens/disposes a connection and transaction inside that worker and never shares them between threads. This is a deliberate SQLite adapter choice, not guidance to wrap future HTTP/PostgreSQL async I/O in Task.Run.

Each call initializes/checks the schema version, then uses a transaction for its operation. Category insertion and task insertion/update commit together. Disposing an uncommitted transaction rolls back changes on validation, SQL errors, or cancellation observed before commit. Failures propagate to callers. Tokens are checked before opening, while loading rows, and immediately before commits; a running native statement or lock wait cannot be instantly canceled. The connection has a five-second busy timeout. Cancellation arriving after commit cannot undo a successful write.

Reads load categories and tasks into a fresh graph, restoring IDs, UTC timestamps, and shared category references. Returned entities are detached snapshots: later writes do not update them, and editing them does not save data. Refresh after a write. Create returns only the created task and its category reference; use a read query for a complete category/task graph. No live object cache is maintained.

For this small dataset, reads and task edits load the current graph, and both stores use TaskQueries for identical LINQ filter/order behavior. Transactions serialize read-modify-write operations, avoiding lost writes from separate service instances; this is not user-visible conflict handling or distributed sync. SQL-level paging and narrower queries can be introduced if real data volume warrants them.

Assigning a category saves its ID/name if new. Reusing an existing ID uses the stored name instead of overwriting it from a stale caller object. Inputs are not attached or mutated, including on failed writes. Category rename/delete operations remain outside ILocalTaskService. Task deletion retains the category row; SQLite's category FK uses ON DELETE SET NULL for any future category deletion operation.

The MVP is create/view/edit/delete, completed/uncompleted, title/description, optional due date/category, priority, five basic filters, SQLite persistence, MVVM, DI, and light/dark themes. Notification scheduling, authentication, network synchronization, and advanced task relationships are later work.

## Offline-first strategy (future)

```text
MAUI ViewModel -> local task service -> SQLite
                                        |
                               future sync worker
                                        |
                                HTTP DTOs -> API -> EF Core -> PostgreSQL
```

SQLite is the task screens' persistent read/write source. ILocalTaskService separates ViewModels from storage, and no task operation requires network availability.

Later, write a task change and an outbox entry in one local transaction. Queue creates/edits/deletions, retain deletion tombstones until acknowledged, preserve client-generated IDs, and use idempotent uploads plus a server change cursor for downloads. Connectivity events can trigger attempts, but successful requests determine reachability. Add cancellation, retry/backoff, and sync status at that phase.

Use server-issued revision tokens for optimistic concurrency. Do not decide conflicts solely from device timestamps: device clocks drift. Initially surface conflicting revisions for an explicit resolution policy; document that policy before coding it. Registration/login, server-enforced user ownership, and secure token storage must exist before shipping synchronization for multiple users.

No synchronization stubs, background jobs, or outbox tables exist in Phase 1. Parent/subtask hierarchy and task dependencies will be distinct relationships in future phases.

## Build boundaries

`TodoApp.slnx` includes every project. `TodoApp.Core.slnf` selects the four SDK-only projects from that same solution for machines without mobile workloads. It is an explicit partial validation path. A passing core build cannot validate XAML, platform manifests, device deployment, or themes. See development progress for actual results.

The tests link the actual portable ViewModel/model/interface source files from the MAUI project, rather than maintaining test copies. Those classes use only .NET APIs; native pages/navigation are compiled by the Windows MAUI build and checked separately. Sixteen presentation cases test editing, validation, retry, cancellation of edits, confirmation, missing tasks, busy/double-save behavior, filtering, and navigation failures.

This checkout has an ignored portable SDK/workload under .tools/dotnet because the machine-wide install was canceled during elevation. scripts/Run-Windows.ps1 selects it when present. TodoAppTargetFrameworks limits only the client; overriding the generic TargetFrameworks property retargeted referenced libraries and caused NETSDK1005. The Windows build bundles Windows App SDK files using WindowsAppSDKSelfContained for unpackaged development launch ([deployment guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)). A fresh clone still needs an SDK/platform workload installation; local tools are not committed.
