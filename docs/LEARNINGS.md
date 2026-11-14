# Learnings

Only concepts used in this project are recorded here. Examples describe the current implementation rather than future frameworks.

## Solution and project reference

**Definition:** A solution groups projects. A project reference lets one project use another's types and establishes build order.

**Short example:** Infrastructure's project file references `..\TodoApp.Domain\TodoApp.Domain.csproj`. Domain has no reverse reference.

## Nullable reference types

**Definition:** Compiler analysis distinguishes references that may be null from those expected to hold a value. It helps catch missing checks; it is not runtime validation.

**Short example:** `CreateWindow(IActivationState? activationState)` accepts a possibly null activation state. `<Nullable>enable</Nullable>` enables analysis.

## File-scoped namespace

**Definition:** A namespace declaration ending in a semicolon applies to the file without an extra indentation block.

**Short example:** `namespace TodoApp.Maui.ViewModels;`

## Dependency injection and lifetime

**Definition:** A container supplies constructor dependencies. Transient registration creates a fresh instance whenever it is resolved.

**Short example:** `builder.Services.AddTransient<Views.WelcomePage>();` lets MAUI construct the page and its constructor-injected WelcomeViewModel.

## MVVM

**Definition:** A View presents UI; a ViewModel exposes presentation state and eventually commands; models/services represent data and operations.

**Short example:** WelcomePage receives WelcomeViewModel and assigns it to `BindingContext`. Its text is bound instead of assigned in UI event handlers.

## XAML binding and compiled binding types

**Definition:** XAML describes a view declaratively. Binding reads a property from its binding context; `x:DataType` provides the expected type for binding compilation.

**Short example:** `<Label Text="{Binding Title}" />` reads `WelcomeViewModel.Title`.

## Theme-aware resources

**Definition:** `AppThemeBinding` selects a value using the operating system's current light/dark theme.

**Short example:** `BackgroundColor="{AppThemeBinding Light=White, Dark=#121212}"` appears on WelcomePage. Device verification is pending.

## Controllers and attributes

**Definition:** ASP.NET Core controllers handle routed HTTP requests. Attributes supply framework metadata for routing and API behavior.

**Short example:** `[Route("api/health")]` plus `[HttpGet]` makes HealthController.Get handle `GET /api/health`.

## Middleware and Problem Details

**Definition:** Middleware forms the HTTP pipeline. Problem Details provides a standard shape for API error responses.

**Short example:** `AddProblemDetails()` registers the response service; `UseExceptionHandler()` handles otherwise unhandled request exceptions. Input-specific errors will need explicit validation when inputs exist.

## async/await and disposal

**Definition:** Awaiting an asynchronous operation lets execution resume when it finishes. `using`/`await using` releases disposable resources reliably.

**Short example:** The integration test uses `await client.GetAsync("/api/health")` and `await using var factory = new WebApplicationFactory<Program>();`.

## Integration test

**Definition:** A test that exercises cooperating components, rather than one isolated method.

**Short example:** ApiStartupTests starts the actual ASP.NET Core application in memory and checks routing, controller registration, HTTP status, and JSON. It does not test PostgreSQL or device deployment.

## Entity identity and private setters

**Definition:** An entity has a stable identity. Private setters allow its methods to protect values from invalid outside assignments.

**Short example:** TaskItem generates `Guid Id` once; `public string Title { get; private set; }` can change only through validated domain methods.

## Enum validation

**Definition:** An enum gives names to numeric values, but a cast can still produce an undefined value. Validate inputs at the domain boundary.

**Short example:** TaskPriority.Normal is 1; `Enum.IsDefined(priority)` rejects `(TaskPriority)99`.

## DateOnly and DateTimeOffset

**Definition:** DateOnly represents a calendar date. DateTimeOffset represents a time with a UTC offset.

**Short example:** `DateOnly? DueDate` can be absent; `CreatedAt = DateTimeOffset.UtcNow` records creation in UTC. A due date does not contain a reminder time.

## Read-only collections and relationships

**Definition:** A read-only wrapper allows viewing a collection without letting callers directly add/remove entries. Relationship methods keep linked objects consistent.

**Short example:** `Category.Tasks` returns `tasks.AsReadOnly()`. `task.SetCategory(category)` updates both the task reference and category collection.

## Validate before mutation

**Definition:** Check every input before changing an object's fields so a rejected operation cannot leave a partially edited object.

**Short example:** UpdateDetails normalizes title/description and validates priority before assigning any properties. A bad priority leaves even a valid replacement title unapplied.

## Idempotent state changes

**Definition:** Repeating the same request leaves the object in the same state without producing another effective change.

**Short example:** After `task.SetCompleted(true)`, calling it again preserves UpdatedAt. Reopening with `SetCompleted(false)` is an effective change.

## Parameterized tests

**Definition:** An xUnit Theory runs a rule against several inputs supplied by InlineData.

**Short example:** Missing-title tests cover null, empty, and whitespace-only strings; the domain rejects all three. Fact tests cover specific workflows such as completion and reopening.

## Interface and implementation

**Definition:** An interface describes operations without choosing how they work. Callers can use another implementation without changing those operation calls.

**Short example:** `ILocalTaskService service = new InMemoryLocalTaskService();` uses memory today. A SQLite implementation will implement the same contract later.

## Record input model

**Definition:** A record groups related values with value-based equality. A positional record gives its inputs named properties, avoiding a long list of service method arguments.

**Short example:** `new TaskDetails("Study", Priority: TaskPriority.High)` supplies local editable fields. It is not a stored entity or an API DTO; its Category reference still points to a mutable object.

## Dictionary and TryGetValue

**Definition:** A dictionary stores values by unique keys. TryGetValue checks whether a key exists and returns its value without a missing-key exception.

**Short example:** `tasks.TryGetValue(id, out var task)` retrieves an existing task; missing tasks return null or false through the service contract.

## LINQ filtering and ordering

**Definition:** LINQ describes queries over sequences. Where selects matching elements; OrderBy/ThenBy establish ordering.

**Short example:** `tasks.Values.Where(task => !task.IsCompleted)` selects pending tasks. The service sorts by title and then ID before materializing the result.

## Materialization and live references

**Definition:** ToList executes a sequence query and stores its current membership. It does not clone the objects inside the list.

**Short example:** In-memory GetTasksAsync keeps its membership after another task is added, but its TaskItem instances are live objects. SQLite instead rebuilds detached objects on every read. An editor should keep unsaved fields separately and use service methods to save them in either case.

## Completed tasks versus asynchronous I/O

**Definition:** A Task<T> can already have a result. An in-memory operation need not occupy a background thread or pretend to perform I/O just to match an asynchronous contract.

**Short example:** `Task.FromResult(result)` returns the in-memory query result. Microsoft.Data.Sqlite has synchronous native I/O even behind its async methods, so this adapter uses Task.Run to move short database operations off the UI thread.

## CancellationToken

**Definition:** A token lets a caller request that work stop cooperatively. Code must observe the token to honor it.

**Short example:** `cancellationToken.ThrowIfCancellationRequested()` runs before operations and before SQLite commits. Cancellation rolls back work when observed before commit; it cannot instantly stop a native SQLite statement.

## TimeProvider and deterministic date tests

**Definition:** TimeProvider supplies time and local time-zone information. A test-specific implementation makes calendar behavior predictable.

**Short example:** `DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime)` computes Due Today. Tests use fixed UTC instants with UTC+8/UTC-8 to verify that local dates, rather than UTC dates, drive the filter.

## Connection and parameterized SQL

**Definition:** A database connection accesses a database. SQL parameters carry values separately from SQL syntax, protecting quotes and user text from being interpreted as commands.

**Short example:** `command.Parameters.AddWithValue("$title", task.Title)` supplies the value used by `$title` in INSERT/UPDATE. Even SQL-looking task titles are stored as data.

## Transaction and rollback

**Definition:** A transaction commits a group of changes together. Disposing it without committing rolls those changes back.

**Short example:** Inserting a new category and its task happens in one transaction. A test forces the task INSERT to fail and verifies that neither row remains.

## Foreign key and delete behavior

**Definition:** A foreign key links a row to a valid row in another table. Delete behavior defines what happens to the link when the referenced row is removed.

**Short example:** TaskItems.CategoryId references Categories.Id with ON DELETE SET NULL. Removing a category at the database level preserves its tasks as uncategorized.

## Hydration and internal access

**Definition:** Hydration rebuilds objects from stored values while retaining identity. Internal members can be shared with a specific assembly using InternalsVisibleTo.

**Short example:** Infrastructure calls `TaskItem.Restore(...)` when reading saved IDs/timestamps. Public callers still cannot set task identity or creation time.

## Schema versioning

**Definition:** A schema version identifies the database layout. Version-specific migrations let future code upgrade existing data deliberately.

**Short example:** `PRAGMA user_version = 1` records the initial schema inside its creation transaction. The app rejects newer unknown versions instead of deleting their data.

## Invariant storage formats

**Definition:** An invariant representation stays the same regardless of the device's display language or date conventions.

**Short example:** DueDate is written as `yyyy-MM-dd` with CultureInfo.InvariantCulture; DateTimeOffset uses round-trip `O` formatting to retain timestamp precision and offset.

## Detached snapshots

**Definition:** A detached object represents a read result without automatic change tracking or save behavior.

**Short example:** Changing a task returned by SqliteLocalTaskService does not update the database. Call UpdateAsync with TaskDetails, then query again to refresh the UI.

## INotifyPropertyChanged and SetProperty

**Definition:** This interface tells a bound UI that a property's value changed. A helper compares the old/new values before raising the notification.

**Short example:** TaskEditorViewModel.Title calls SetProperty. ErrorMessage also notifies HasError so the error label appears when needed.

## ObservableCollection<T>

**Definition:** A collection that notifies the UI when entries are added or removed. It does not automatically make each object's properties observable.

**Short example:** `ObservableCollection<TaskRow> Tasks = new();` is exposed by TaskListViewModel. Refresh replaces rows with new display snapshots after saved changes.

## ICommand and async command execution

**Definition:** ICommand exposes an action, whether it can run, and an event telling the UI to reevaluate that state. An async command can await work while preventing duplicate execution.

**Short example:** SaveCommand runs SaveAsync once at a time. Its Execute method is an async event boundary with exception handling; operation methods return Task for tests to await.

## Separate editor state

**Definition:** A form holds draft fields independently from saved data, so changing controls is not itself a persistence operation.

**Short example:** Editing TaskEditorViewModel.Description does not alter the task entity. Save constructs TaskDetails; Cancel navigates back without writing.

## Dependency injection scope

**Definition:** A scope defines which registered objects share a lifetime. Different scopes receive different scoped instances.

**Short example:** App.CreateWindow creates one scope per window, giving it its own list/navigation service; editor instances are transient, and the SQLite service remains singleton.

## Nullable date binding

**Definition:** A nullable control value represents an optional input without inventing a sentinel date.

**Short example:** The .NET 10 DatePicker binds DateTime? DueDate. Clear sets it to null, while Save converts a selected value to DateOnly for the domain.

## Presentation boundary and test doubles

**Definition:** A small interface can separate UI-specific interactions from presentation logic so tests can supply predictable behavior.

**Short example:** ITaskNavigation handles editor navigation and delete confirmation. Tests return true/false for confirmation and simulate failed navigation without opening a native window.

## DTO (Data Transfer Object)

**Definition:** A DTO defines the data allowed across an application boundary without exposing the full domain entity.

**Short example:** TaskWriteRequest accepts editable fields; TaskResponse returns a flat categoryId/categoryName instead of Category.Tasks navigation graphs. Id/CreatedAt/UpdatedAt are response-only.

## API model validation

**Definition:** Model validation checks deserialized input before controller logic executes. ApiController automatically returns a 400 validation response when model state is invalid.

**Short example:** TaskWriteRequest implements IValidatableObject to validate trimmed text lengths. See [ASP.NET Core API behavior](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0).

## Unknown JSON member rejection

**Definition:** A JSON contract can reject fields that have no corresponding allowed property, helping detect client mistakes and attempts to set server-owned values.

**Short example:** JsonUnmappedMemberHandling.Disallow on TaskWriteRequest makes a supplied createdAt field a 400 error instead of silently ignoring it.

## HTTP resource responses

**Definition:** HTTP status codes communicate the result of a resource operation independently of its response body.

**Short example:** CreatedAtAction returns 201 plus the GET URL of a created task. DELETE returns 204 with no body; an absent task returns 404.

## PUT replacement semantics

**Definition:** A full-update request describes the editable state to retain, rather than only a subset of fields to change.

**Short example:** PUT with only a title clears optional fields, sets Normal priority, and sets IsCompleted to false. Include fields whose values should remain unchanged.

## Locking shared server state

**Definition:** A lock ensures only one thread enters a critical section at a time, protecting shared mutable collections.

**Short example:** The singleton InMemoryTaskService locks task/category updates and builds response snapshots within the lock. It never awaits while holding that lock.

## String enum JSON

**Definition:** A JSON enum converter represents enum values by their names rather than numeric backing values.

**Short example:** Priority is sent as `"High"`. JsonStringEnumConverter<TaskPriority> with allowIntegerValues false rejects numeric priority input.

## DbContext and DbSet

**Definition:** DbContext coordinates EF queries, tracking, and saving. DbSet is a query/update entry point for one entity type.

**Short example:** TodoDbContext.Tasks represents stored TaskItem rows; await database.SaveChangesAsync(token) saves tracked changes. Each API request receives its own scoped context.

## Fluent entity configuration

**Definition:** EF's Fluent API maps classes, columns, relationships, and constraints outside the domain types.

**Short example:** TaskItemConfiguration maps CategoryId as an optional FK with DeleteBehavior.SetNull. CategoryConfiguration maps the private tasks collection field.

## Tracking and AsNoTracking

**Definition:** EF tracks loaded objects when it needs to detect updates. AsNoTracking reads avoid that state for read-only results.

**Short example:** GET operations load tasks with AsNoTracking plus Include(Category), then build response DTOs. PUT uses tracked entities and domain mutation methods before saving.

## Code First migrations and model snapshot

**Definition:** A migration records a model's schema changes as reviewable code. The snapshot describes the model used to compare future changes.

**Short example:** InitialCreate creates Categories/Tasks and their FK/index/check constraints. dotnet ef database update applies pending migrations explicitly; API startup does not modify the schema.

## Shadow property and unique index

**Definition:** A shadow property exists in the EF model/database without a property on the domain class. A unique index prevents duplicate stored keys.

**Short example:** Category's NameKey stores its uppercase-invariant name for server uniqueness. EF.Property<string>(category, "NameKey") queries it.

## Atomic upsert

**Definition:** An insert with a database conflict rule handles competing creators as one atomic operation.

**Short example:** Category insertion uses parameterized INSERT ... ON CONFLICT (NameKey) DO NOTHING. Concurrent requests then read the same existing category instead of creating duplicates.

## Development user secrets

**Definition:** User secrets keep development configuration outside the repository. Environment variables can supply the same configuration key.

**Short example:** The API reads ConnectionStrings:TodoDatabase; its environment spelling is ConnectionStrings__TodoDatabase. The connection never belongs in MAUI or committed appsettings.

## Database timestamp precision

**Definition:** Databases and .NET can represent timestamps at different resolutions, so a saved value may otherwise differ slightly when read back.

**Short example:** TodoDbContext rounds down UTC timestamps to PostgreSQL microseconds before saving, so response timestamps round-trip exactly.

## Password hashing

**Definition:** A password hasher stores a salted, computationally expensive verification value instead of the plaintext password. Use a maintained framework implementation rather than inventing password cryptography.

**Short example:** AuthService uses ASP.NET Core PasswordHasher<UserAccount> to hash/register and verify/login, with rehash support when required. Password input is never trimmed.

## JWT bearer validation

**Definition:** A bearer token grants access to whoever possesses it. The server must validate its signature, issuer, audience, and lifetime before trusting claims.

**Short example:** JwtBearer validates an HS256 token, then HttpCurrentUser reads its validated sub claim. Merely decoding a JWT is not authentication.

## Authentication versus authorization

**Definition:** Authentication establishes who the caller is; authorization decides which operations/data that caller can access.

**Short example:** Authorize requires a valid token, while PostgresTaskService additionally filters OwnerId. A valid token does not grant access to another user's task ID.

## Claim-derived ownership

**Definition:** Ownership comes from authenticated server context, not a user-supplied JSON property.

**Short example:** New task OwnerId is set from ICurrentUser.UserId, and another user's resource returns 404. The request DTO rejects ownerId/categoryId input.

## Rate limiting

**Definition:** A rate limiter caps requests over a time window to reduce abusive load. Its scope and limits must be understood.

**Short example:** Register/login share 10 requests per IP per minute and return 429 after that. This process-local limiter is not distributed throttling or account lockout.

## SecureStorage and local logout

**Definition:** MAUI SecureStorage uses platform-protected storage for small sensitive values. Removing a local token does not invalidate every copy of an already issued JWT.

**Short example:** SecureTokenStore stores/removes a protected session envelope containing token, expiry, account, and API origin. Phase 9 provides login/logout UI with no plaintext fallback or password storage; server tokens expire after their short lifetime.

## Preserving data during ownership migrations

**Definition:** New ownership rules must not silently delete old rows or guess who owns anonymous data.

**Short example:** AddAuthenticationOwnership leaves legacy OwnerId null and excludes those rows from account queries. An administrator must explicitly review any later assignment.

## Per-request authentication and origin binding

**Definition:** A shared HTTP transport can serve many operations, but credentials belong to one captured request/session and one trusted API origin.

**Short example:** TodoApiClient sets Authorization on each HttpRequestMessage. Changing the HTTPS endpoint signs out; redirects are disabled so the handler cannot forward a request automatically. Client DTOs serialize only editable fields, not server ownership or IDs.

## Stale asynchronous results

**Definition:** An awaited operation may finish after the user has changed accounts. Its result must still belong to the active session before changing UI or storage.

**Short example:** ClientSession increments a version on account changes; old task responses and old 401 failures cannot populate or sign out a new account. Secure-storage writes are serialized, and logout blocks restoration before attempting removal.

## Uncertain write outcomes

**Definition:** A timeout or lost response does not prove that the server failed to save a request. Blind retries can create duplicates.

**Short example:** The server workspace requires a successful explicit refresh after an uncertain write. A confirmed create clears its draft before the follow-up read. Phase 10 adds a separate synced workspace whose durable operation IDs make retries safe.

## Online integration versus synchronization

**Definition:** Calling a server from a client does not reconcile two databases or make server operations available offline.

**Short example:** Account & server manages online PostgreSQL tasks. Synced tasks uses a separate per-account cache/outbox for reconciliation. Back returns to device-only SQLite tasks, which login never claims or uploads.

## Test transport boundaries

**Definition:** An in-memory request host verifies HTTP contracts and middleware without proving certificates, native networking, or OS storage behavior.

**Short example:** The real client passes registration/CRUD/JWT/PostgreSQL tests through WebApplicationFactory, while actual Windows/mobile HTTPS and SecureStorage restart acceptance remain manual checks.

## Transactional outbox and immutable operations

**Definition:** Save local state and its outgoing request in one transaction, so a restart cannot retain an edit without retaining the work needed to upload it.

**Short example:** SqliteSyncStore saves Local plus Pending together. Pending keeps its original operation ID/payload after a lost response; later edits become the next operation after acknowledgement.

## Idempotency receipts and revisions

**Definition:** An operation receipt makes a repeated upload produce the same result. A server revision identifies the state an edit was based on; a mismatch requires conflict resolution.

**Short example:** SyncService checks an owner/operation receipt before writing. BaseRevision must match the current record. Task, revision, and receipt commit together under the account transaction lock.

## Tombstones and download cursors

**Definition:** A tombstone retains evidence of deletion for clients that were offline. A cursor records the highest downloaded revision applied locally.

**Short example:** SyncRecords retains a Deleted entry after its task row is removed. Applying a page saves that entry and the cursor in one SQLite transaction; the client does not resurrect a deleted row by forgetting the deletion.

## Stale receipt ordering

**Definition:** A retry response may describe an earlier successful write than a state already downloaded. Successful delivery does not make that response the newest state.

**Short example:** A lost create reply at revision 1 can be replayed after another device's revision 2 was downloaded. The cache keeps revision 2; any later local edits become a conflict instead of being silently rebased over it.

## Explicit conflicts and bounded retries

**Definition:** Keep both versions until the user chooses a policy, and space failed requests instead of looping continuously.

**Short example:** Keep mine creates a new operation against the displayed server revision; Use server discards the saved local intent. Failed uploads retain their operation IDs and retry from 4 seconds up to 5 minutes. Connectivity is a hint; a real request determines reachability. The full protocol and native acceptance steps are in SYNCHRONIZATION.md.

## Calendar dates versus reminder instants

**Definition:** A due date identifies a calendar day. A reminder needs an explicit instant and a policy for translating a local wall clock into UTC.

**Short example:** ReminderTime.Resolve rejects skipped/repeated DST times and stores a fixed UTC instant plus the original time zone. Traveling changes its local display, not when it fires. A due date by itself never schedules an alert.

## Permission and platform capabilities

**Definition:** Permission to notify and the ability to schedule while closed are separate platform capabilities. Neither guarantees the user saw a notification.

**Short example:** IReminderPlatform separates permission, scheduling, showing, and canceling. The unpackaged Windows build evaluates reminders while running; Android/Apple adapters schedule with their OS. Permission prompts happen only from the explicit Reminders button.

## Persisting intent before side effects

**Definition:** Save what the user requested before making an external/native call, and expose failures without confusing them with task persistence.

**Short example:** A reminder's cancellation is durable even if native removal fails, so reconciliation can retry it. Windows records its delivery attempt before showing to avoid duplicate alerts after an uncertain crash. Task writes remain successful when notification operations fail.

## Post-commit reconciliation

**Definition:** Update dependent local behavior after a successful write while keeping its failure boundary separate from the original transaction.

**Short example:** ReminderAwareTaskService decorates local writes, and SqliteSyncStore.Written notifies after commit. The reminder catalog reads concrete stores without triggering another write callback. A callback failure cannot make a committed create look unsaved.

## Native acceptance versus portable tests

**Definition:** Tests using a fake OS adapter can verify scheduling decisions and failure policies without proving actual permissions, timers, or system delivery.

**Short example:** The reminder suite verifies UTC/DST, durable IDs, caps, cancellation races, permission denial, and uncertain attempts. Windows compilation checks API/XAML references; NOTIFICATIONS.md separately tracks Windows display and Android/Apple native build/device acceptance.

## Query definitions versus task copies

**Definition:** A saved view stores rules that select current records, rather than copying the records that matched when the view was created.

**Short example:** Saving Pending + High + Today retains that rule in views.db3. Reapplying tomorrow recomputes the device's Today against current device/cache rows. Removing the view does not remove any matching task.

## Composable predicates and deterministic order

**Definition:** Independent predicates can combine with AND, while explicit sort/tie-break rules keep results stable.

**Short example:** TaskViewQueries combines literal search (OR across title/description/category) with completion, priority, category, and due rules. DueDate sorts undated tasks last, then title and ID break ties. No regex, SQL predicate text, or mutable task graph is needed.

## Editable draft versus applied query

**Definition:** Changing controls does not need to immediately change the result set. Keeping both states explicit avoids surprising partial filters.

**Short example:** Selecting a saved view fills the draft controls. Apply or a successful explicit save updates results. Refresh recomputes the applied rule, and validation/conflict failures retain the unfinished draft.

## Local revisions for settings

**Definition:** Optimistic concurrency is useful for shared local preferences as well as server task edits.

**Short example:** Two windows can load the same saved definition. Its ID/revision must still match when updating or deleting; otherwise the user refreshes/reviews instead of silently replacing another window's changes.

## Incremental feature boundaries

**Definition:** A large roadmap phase can consist of independently reviewable model/storage/UI increments.

**Short example:** Phase 12A adds saved rules over existing task fields. Tags, hierarchy, dependency cycles, recurrence, focus history, and desktop panes each still need their own design and validation; category text is not used to pretend tags already exist.

## Immutable collections with value equality

**Definition:** A collection used as a value should compare its contents and should not change through a retained array/list reference.

**Short example:** TagSet normalizes and privately copies labels, serializes as a JSON array, and compares canonical contents. A new deserialized tag set equals the acknowledged intent, so sync does not queue another operation indefinitely.

## Absent fields versus explicit clearing

**Definition:** Versioned contracts need an explicit policy for fields older callers do not send.

**Short example:** Omitted/null tags preserve existing values on update; [] clears them. Null tags are omitted from legacy sync serialization, keeping stored receipt request bytes compatible. This exception is deliberate and differs from the other PUT fields' original defaulting rules.

## Capability checks before writes

**Definition:** A client should know that an endpoint understands a feature before sending a feature-specific write.

**Short example:** Tagged sync operations require ProtocolVersion 2. An older API causes a retained, blocked operation and an upgrade message; Retry now reuses its operation ID after upgrading.

## Per-task labels versus global entities

**Definition:** A bounded label set need not imply a global catalog with IDs, ownership, rename, and deletion relationships.

**Short example:** Tags are normalized task values stored as JSON text. Categories keep their existing entity relationship, and tag filtering uses exact set membership. A global tag-management feature would need its own reviewed model and migration.

## A hierarchy is different from a dependency graph

**Definition:** A hierarchy gives each task one optional parent; a dependency graph models prerequisites, potentially with multiple edges.

**Short example:** A child under Project can be completed independently. A future blocked-by edge needs a separate completion/blocking policy. Parent nesting does not imply a dependency.

## Foreign keys do not prevent cycles

**Definition:** A self FK ensures a referenced task exists, but cannot alone reject A-under-B/B-under-A or enforce an ancestor depth limit.

**Short example:** Validate the moved subtree and ancestor paths inside the write transaction. The server's per-owner lock ensures a concurrent opposite move sees the first committed move and rejects the cycle. Count descendants when enforcing the 16-level limit.

## Missing input versus explicit root

**Definition:** An additive nullable wrapper distinguishes an old caller omitting a feature from an intentional relationship clear.

**Short example:** Missing Parent preserves placement; TaskParent(null) detaches. Omitting absent Parent from legacy JSON also preserves exact receipt request bytes.

## Order operations and preserve rejected intent

**Definition:** Related writes need ordering, while concurrent invalid structure needs review rather than an endless blind retry.

**Short example:** Upload a new parent before its child link and delete children before the parent. If another replica makes a move cyclic, keep its local fields as a conflict. Confirm a valid new parent against the displayed revision, then queue a fresh operation. See HIERARCHY.md.

## Completion gates versus permanent invariants

**Definition:** A transition gate checks whether an action may happen now; a permanent invariant would need to remain true after every related change.

**Short example:** Complete B only when its prerequisites are complete. Reopening A later leaves completed B unchanged. Reopen B explicitly and its next completion is checked again. This avoids an implicit state cascade.

## Dependency graphs and storage boundaries

**Definition:** Multiple outgoing prerequisites form a directed graph, independently of a tree's one optional parent. Iterative active/finished traversal detects cycles while permitting shared prerequisites.

**Short example:** A and B may both require C; D may require both A and B. Same-workspace membership and cycles are validated in the write transaction. JSON text stores a bounded immutable set but cannot provide per-ID foreign keys, so service validation/locking and administrative SQL discipline remain essential. Dependency-only operations preserve unrelated fields; null means preserve and [] means clear. See DEPENDENCIES.md.

## Unknown is different from false

**Definition:** An unset planning decision should not be stored as if both urgency and importance had already been answered No.

**Short example:** Unclassified has unknown axes; Neither explicitly means not urgent and not important. A five-state enum stores valid combinations without partial Boolean states. Priority and deadline stay independent.

## Additive classification across replicas

**Definition:** Nullable command input distinguishes preserve from an intentional reset while a stable stored enum represents the actual task value.

**Short example:** Omitted/null Quadrant preserves; explicit Unclassified clears. Old receipt bytes omit null Quadrant, protocol 5 guards new writes, and immutable outgoing payloads keep later classifications as separate operations. Saved-view Any is null, which differs from an Unclassified filter. See EISENHOWER.md.

## Calendar anchors versus repeated addition

**Definition:** Calculate a monthly occurrence from its original anchor instead of adding one month to the previous clamped date.

**Short example:** January 31 plus one anchored month is February 28; plus two is March 31. Repeatedly adding to February 28 would drift to March 28. DateOnly has no instant/time-zone conversion; reminder instants stay separate.

## Idempotent completion and retained counters

**Definition:** A retry must identify the occurrence it meant to complete rather than mean complete whatever is current now.

**Short example:** An expected RepeatState advances counter 0 to 1. Retrying that state returns current counter 1 without another advance. The server retains a counter floor after deletion so restoring the same ID cannot reset consumed work. A same-counter rule change requires review. Sync combines this with owner transactions, base revisions, receipts, and frozen outgoing operations.

## Choose a scheduler only for a timed action

**Definition:** A scheduler is justified by a required time-triggered job, not merely by the word recurring.

**Short example:** Completion-based recurrence already has a current future task and advances in an explicit user transaction. No timed task-generation job is needed. Timed expansion/background jobs would require separate scheduling/idempotency/retention design. See RECURRENCE.md.

## Elapsed time is not a wall-clock difference

**Definition:** A monotonic timestamp measures duration; UTC timestamps describe when observations were recorded and may jump when a clock changes.

**Short example:** Focus TimeSpent uses TimeProvider.GetElapsedTime. Moving the wall clock backwards cannot remove credited ticks; moving it forwards cannot add hours. Long heartbeat gaps interrupt instead of guessing. UTC labels and an increasing ledger sequence are separate metadata.

## Derive totals from stable ledger entries

**Definition:** A durable session row is the source of truth; a displayed total is a query over those rows.

**Short example:** Checkpoints update one session ID with cumulative elapsed ticks. Retrying Stop does not add time again. TimeSpent sums Focus rows, excludes breaks/paused gaps, and rounds only for display. Task/recurrence snapshots keep history after deletion or rescheduling.

## Live state versus a recoverable checkpoint

**Definition:** A timer can have a small unsaved tail, but recovery should never infer unseen work from the time an app was closed.

**Short example:** Five-second checkpoints bound crash loss. Restart marks the abandoned session Interrupted at its saved timestamp; no auto-resume or closed-app duration is credited. An OS process lease, one-active SQL index, revisions, and account guards solve different concurrency problems. See FOCUS.md.

## Phase 12G - Adaptive navigation preserves state rather than recreating it

**Concept:** Available logical width chooses layout; the ViewModel owns task/category/search selection independently. Read-only details survive resize and successful refresh by stable task ID. A filter or deletion that removes that ID must clear details and disable its commands.

**Why it matters:** Device type cannot predict a desktop window's size or a phone's orientation. Rebuilding pages when the width changes can lose selections or unsaved editor drafts. Existing editor pages and service validation remain the write boundary; the root layout only changes visibility and column widths.

**Short example:** A selected Work task stays open when a window crosses 1180 logical pixels. Searching for unrelated text clears it; Complete cannot then operate on a hidden task. Case-insensitive category keys distinguish virtual groups from real categories with reserved-looking names. Counts describe the service filter before local search. Isolated Debug preview data makes visual checks possible without touching the user's normal task files or saved account session.

## Phase 13A - Calendar intervals and data exchange

**Term:** Half-open time interval. **Definition:** The start is included and the end is excluded. Two blocks overlap when first.Start < second.End and second.Start < first.End. **Short example:** 09:00-09:30 and 09:30-10:00 are adjacent, not overlapping. Checking this inside the same transaction as the write prevents two windows from both reserving the same slot.

**Term:** Wall time versus elapsed duration. **Definition:** A local calendar clock value needs a time zone to become an instant; elapsed minutes are measured on the UTC timeline. **Short example:** A two-hour block that starts before a daylight-saving jump still lasts 120 minutes even if its displayed end clock is three hours later. Skipped/repeated starts require a different unambiguous selection.

**Term:** iCalendar UID and content-line folding. **Definition:** A stable UID identifies an exported event, while folding wraps long UTF-8 content lines at 75 octets without splitting a Unicode character. **Short example:** Exporting a rescheduled block keeps its UID and increases SEQUENCE. Escaped newlines cannot inject another VEVENT. Import is a copy operation; stable IDs alone do not implement provider synchronization.

## Phase 13B - OAuth is a separate authorization boundary

**Term:** PKCE and state. **Definition:** PKCE binds an authorization code to the client attempt with a secret verifier and its SHA-256 challenge; state binds the browser callback to the expected request. **Short example:** A code copied into a callback with the wrong state is rejected before token exchange, and an intercepted code lacks the verifier needed by the token endpoint.

**Term:** Stable subject versus display email. **Definition:** A provider subject identifies the Google account; an email label can change. **Short example:** UserInfo must match the saved subject after refresh. A new subject requires explicit disconnect/reconnect rather than silently replacing another Google identity.

**Term:** Fail-closed restoration marker. **Definition:** Persist a non-secret block before replacing/removing a protected credential and unblock only after successful save. **Short example:** If SecureStorage deletion fails, a blocked stale refresh token cannot be restored on restart. If the marker fails too, in-process blocking remains and the UI reports the unresolved restart risk.

**Term:** Capture context before a serialization gate. **Definition:** An operation must keep the account that authorized it while waiting for another window. **Short example:** A queued Google Connect for Todo account A is rejected after switching to B, instead of capturing B when it finally acquires the semaphore.

## Phase 13C - A remote side effect needs durable identity

**Term:** Durable pending operation. **Definition:** Save the intended provider identity and immutable payload before sending a network write. **Short example:** If Google creates an event but its reply is lost, retry reads the same reserved event ID and verifies ownership/title/times instead of allocating another event. New local edits wait until that captured operation finishes.

**Term:** Conditional modification with ETag. **Definition:** If-Match asks a provider to change only the exact version previously acknowledged. **Short example:** A remote edit between GET and PATCH returns 412; the journal becomes Conflict and the app does not overwrite the unseen version. A confirmed deletion follows the same rule.

**Term:** Identity domains. **Definition:** A local block ID, iCalendar UID and Google event resource ID identify different objects. **Short example:** Importing an ICS file does not create a trusted app link. A provider event must have the journal's reserved ID and private ownership markers before the app can change it.

## Phase 13D.1 - A review is a versioned decision input

**Term:** Reviewed snapshot. **Definition:** A displayed comparison must identify the exact local and provider versions that a user approved. **Short example:** If Google changes its ETag or another window edits the local block after review, Use-Google rejects the old choice rather than silently accepting newer fields.

**Term:** Atomic local reconciliation. **Definition:** Commit the adopted local block and its acknowledged provider baseline together after validation. **Short example:** A Google time range that overlaps another local block rejects the transaction; neither block fields nor link metadata advance.

**Term:** Snapshot title versus source identity. **Definition:** A calendar block can adopt a provider title without renaming its source task. **Short example:** TaskId, scope and captured zone stay fixed while the reviewed block title/UTC range changes; task completion and focus history are untouched.

**Term:** Local detach versus provider deletion. **Definition:** Detach closes local management without transmitting a remote deletion. **Short example:** A recurring/foreign event is never forcibly overwritten; the user can detach its local link and separately handle the old provider event before explicitly publishing another.
