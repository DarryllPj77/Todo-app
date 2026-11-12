# Learnings

Only concepts used in this project are recorded here. Add async database, LINQ, and UI collection entries when their implementations arrive.

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
