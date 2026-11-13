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
