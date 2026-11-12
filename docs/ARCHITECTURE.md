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
MauiProgram: register WelcomePage and WelcomeViewModel
  -> App.CreateWindow: resolve a fresh page for each window
     -> NavigationPage -> WelcomePage -> bound WelcomeViewModel properties
```

The immutable welcome properties need no change notifications. When task editing arrives, use `INotifyPropertyChanged` for mutable state, commands for actions, and `ObservableCollection<T>` for UI-bound task collections. Do not put database logic in page event handlers. System light/dark theme resources and `AppThemeBinding` supply initial theme behavior; visual verification is pending.

NavigationPage is intentionally sufficient for one screen. Shell routes and the eventual desktop `Categories | Tasks | Task Details` layout can be introduced when navigation/adaptive layout needs them. On phones, list/detail navigation will replace desktop panes.

## Project references and responsibility

```text
MAUI -------------> Domain
  |                 ^
  +-> Infrastructure+
API --------------> Domain
  +-> Infrastructure
Tests ------------> API, Domain, Infrastructure
```

Domain contains TaskItem, Category, TaskPriority, TaskFilter, and the ILocalTaskService/TaskDetails contract without package dependencies. Infrastructure references Domain and implements InMemoryLocalTaskService. Neither library references MAUI or ASP.NET Core. Both hosts may use Infrastructure, but client and server registrations will select their own persistence implementation; future platform-specific packages can be split if that becomes necessary. No EF/SQLite packages are being shared prematurely.

Dependency injection lets the framework create registered objects and supply constructor dependencies. MAUI registers the welcome ViewModel/page as transient to support separate windows; ASP.NET Core registers controllers through `AddControllers`. ILocalTaskService is now a real boundary exercised by tests. Neither host registers the temporary in-memory implementation: the welcome page does not need task operations yet. Register the persistent implementation with task ViewModels when the UI phase arrives; avoid creating an extra empty repository abstraction.

## Intended algorithm and main features

The current task logic lives in plain domain methods. Construction validates and trims title/description, validates the priority enum, generates an ID, and captures UTC timestamps. UpdateDetails validates all inputs before assigning any fields, so rejected edits leave the task unchanged. Equivalent edits and repeated SetCompleted/SetCategory requests are no-ops. Real edits update UpdatedAt while preserving identity and CreatedAt.

TaskItem.SetCategory removes the task from its previous category and adds it to the new one. Category exposes a read-only collection so callers cannot bypass that relationship logic. CategoryId currently derives from the category reference; persistence mapping and ID-only loading will be designed when storage arrives. No category deletion operation exists yet. Category rename preserves its ID and tasks.

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

The contract returns Task<T> so SQLite can perform real asynchronous I/O later. The in-memory version has no I/O: Task.FromResult supplies completed results without Task.Run, fake delays, or unnecessary async/await. Results have read-only, materialized collection membership, but the TaskItem objects are live references. Callers must use service methods for writes and keep unsaved editor fields separately; these are not detached database snapshots. The temporary service is intended for sequential local use, is not thread-safe, and must not be reused as a concurrent server/sync store. SQLite will define transactions, loading/identity, and ownership at the persistence boundary.

The MVP is create/view/edit/delete, completed/uncompleted, title/description, optional due date/category, priority, five basic filters, SQLite persistence, MVVM, DI, and light/dark themes. Notification scheduling, authentication, network synchronization, and advanced task relationships are later work.

## Offline-first strategy (future)

```text
MAUI ViewModel -> local task service -> SQLite
                                        |
                               future sync worker
                                        |
                                HTTP DTOs -> API -> EF Core -> PostgreSQL
```

SQLite will be the client's immediate read/write source, so the app remains usable without a server. ILocalTaskService already separates callers from the temporary store; the SQLite implementation will replace it. ViewModels should not depend on network availability.

Later, write a task change and an outbox entry in one local transaction. Queue creates/edits/deletions, retain deletion tombstones until acknowledged, preserve client-generated IDs, and use idempotent uploads plus a server change cursor for downloads. Connectivity events can trigger attempts, but successful requests determine reachability. Add cancellation, retry/backoff, and sync status at that phase.

Use server-issued revision tokens for optimistic concurrency. Do not decide conflicts solely from device timestamps: device clocks drift. Initially surface conflicting revisions for an explicit resolution policy; document that policy before coding it. Registration/login, server-enforced user ownership, and secure token storage must exist before shipping synchronization for multiple users.

No synchronization stubs, background jobs, or outbox tables exist in Phase 1. Parent/subtask hierarchy and task dependencies will be distinct relationships in future phases.

## Build boundaries

`TodoApp.slnx` includes every project. `TodoApp.Core.slnf` selects the four SDK-only projects from that same solution for machines without mobile workloads. It is an explicit partial validation path. A passing core build cannot validate XAML, platform manifests, device deployment, or themes. See development progress for actual results.
