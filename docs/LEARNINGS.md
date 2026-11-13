# Learnings

Only concepts used in this project are recorded here. Add UI collection/command entries when task screens arrive.

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
