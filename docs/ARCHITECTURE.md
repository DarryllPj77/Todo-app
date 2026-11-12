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

Domain has no package dependencies and no entities yet. Infrastructure references Domain only. Neither library references MAUI or ASP.NET Core. Both hosts may use Infrastructure, but client and server registrations will select their own persistence implementation; future platform-specific packages can be split if that becomes necessary. No EF/SQLite packages are being shared prematurely.

Dependency injection lets the framework create registered objects and supply constructor dependencies. MAUI registers the welcome ViewModel/page as transient to support separate windows; ASP.NET Core registers controllers through `AddControllers`. There is no empty repository interface or no-op registration extension: these will be added when real behavior needs them.

## Intended algorithm and main features

No task algorithm runs yet. The local MVP will validate task input, save it asynchronously through a local service, query tasks, apply readable LINQ filters, and refresh observable presentation state. Completion changes and timestamp updates will be tested as business behavior. Due Today will compare a date-only due date with the user's local calendar day.

The MVP is create/view/edit/delete, completed/uncompleted, title/description, optional due date/category, priority, five basic filters, SQLite persistence, MVVM, DI, and light/dark themes. Notification scheduling, authentication, network synchronization, and advanced task relationships are later work.

## Offline-first strategy (future)

```text
MAUI ViewModel -> local task service -> SQLite
                                        |
                               future sync worker
                                        |
                                HTTP DTOs -> API -> EF Core -> PostgreSQL
```

SQLite will be the client's immediate read/write source, so the app remains usable without a server. Introduce the local service boundary with actual CRUD; ViewModels should not depend on network availability.

Later, write a task change and an outbox entry in one local transaction. Queue creates/edits/deletions, retain deletion tombstones until acknowledged, preserve client-generated IDs, and use idempotent uploads plus a server change cursor for downloads. Connectivity events can trigger attempts, but successful requests determine reachability. Add cancellation, retry/backoff, and sync status at that phase.

Use server-issued revision tokens for optimistic concurrency. Do not decide conflicts solely from device timestamps: device clocks drift. Initially surface conflicting revisions for an explicit resolution policy; document that policy before coding it. Registration/login, server-enforced user ownership, and secure token storage must exist before shipping synchronization for multiple users.

No synchronization stubs, background jobs, or outbox tables exist in Phase 1. Parent/subtask hierarchy and task dependencies will be distinct relationships in future phases.

## Build boundaries

`TodoApp.slnx` includes every project. `TodoApp.Core.slnf` selects the four SDK-only projects from that same solution for machines without mobile workloads. It is an explicit partial validation path. A passing core build cannot validate XAML, platform manifests, device deployment, or themes. See development progress for actual results.
