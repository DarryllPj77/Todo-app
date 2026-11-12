# Todo App

A learning-focused C#/.NET Todo application, built one phase at a time. The first usable version will be an offline MAUI task manager; a separate API will later provide server persistence and synchronization.

**Current status (2026-11-13): Phase 2 domain models and business rules implemented and tested; MAUI build and runtime verification still await workload setup.** TaskItem, Category, and TaskPriority now define task data and validation. CRUD services/screens, databases, authentication, and synchronization are later phases. See [development progress](docs/DEVELOPMENT_PROGRESS.md) for verified results and the next task.

## Technology and prerequisites

- Installed/selected SDK: .NET **10.0.404**, pinned in `global.json`; latest patch within that feature band is allowed.
- MAUI 10, XAML, MVVM, built-in dependency injection; Microsoft.Maui.Controls **10.0.130**, a stable .NET 10 release checked against NuGet. The official matching template supplied platform files and resources.
- ASP.NET Core 10 controller API using the SDK's shared framework.
- xUnit and ASP.NET Core's in-memory integration test host.
- Planned: SQLite locally, EF Core with Npgsql/PostgreSQL on the server, LINQ filters, and JWT later. Their packages are deliberately deferred until their phases.

Core projects require the selected SDK and NuGet access. MAUI additionally needs platform workloads/tools. On Windows, use Visual Studio with .NET MAUI development support, or the .NET MAUI extension for VS Code and the required native tooling. Android needs its SDK/JDK and a device/emulator. Apple targets require a Mac with compatible Xcode; Windows cannot independently build/run every Apple target.

## Solution structure

```text
TodoApp.slnx                 All five projects, including MAUI
TodoApp.Core.slnf            Same solution, excluding MAUI for SDK-only checks
src/
  TodoApp.Domain/           TaskItem, Category, TaskPriority; Rules reserved
  TodoApp.Infrastructure/   Persistence, Repositories, Services (reserved)
  TodoApp.Api/              Controllers, Services, Data, Configuration
  TodoApp.Maui/             Views, ViewModels, Models, Services, Resources, Platforms
tests/TodoApp.Tests/        Domain business-rule and API startup tests
docs/                      Roadmap, architecture, database plan, learnings, progress
```

The repository folder itself is the solution root; no extra nested `TodoApp/` directory is necessary. `.gitkeep` files preserve reserved folders without inventing implementations.

## Setup and core verification

From the solution root in PowerShell:

```powershell
dotnet --info
dotnet workload list
dotnet restore TodoApp.Core.slnf
dotnet build TodoApp.Core.slnf --no-restore
dotnet test TodoApp.Core.slnf --no-build --no-restore
```

The filter does not convert MAUI into a plain .NET library or claim to validate client code.

## Run the API

```powershell
dotnet dev-certs https --trust
dotnet run --project src/TodoApp.Api --launch-profile https
```

Open `https://localhost:7114/api/health`; expected JSON is `{"status":"healthy"}`. The HTTP listener is `http://localhost:5062`, redirected to HTTPS by the pipeline when the HTTPS profile is used. `src/TodoApp.Api/TodoApp.Api.http` contains the same request. Stop with Ctrl+C.

This endpoint checks application liveness only. No task endpoints, database readiness checks, OpenAPI UI, or authentication exist in this phase. Do not deploy the unauthenticated foundation as a task service.

## Build and run MAUI

Install the workloads using the platform tooling described in the [official MAUI setup guide](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0). For a Windows-first setup, an elevated terminal may be needed:

```powershell
dotnet workload install maui-windows
dotnet build src/TodoApp.Maui/TodoApp.Maui.csproj -p:TargetFrameworks=net10.0-windows10.0.19041.0
dotnet build src/TodoApp.Maui/TodoApp.Maui.csproj -t:Run -p:TargetFrameworks=net10.0-windows10.0.19041.0
```

Overriding `TargetFrameworks` limits restore and build to Windows, avoiding unrelated Android/Apple tooling requirements. The Windows template uses unpackaged deployment. Expect a welcome page with system light/dark theme colors; task management comes later.

For Android, install `maui-android` plus its SDK/JDK, select a device/emulator, and use:

```powershell
dotnet build src/TodoApp.Maui/TodoApp.Maui.csproj -t:Run -p:TargetFrameworks=net10.0-android
```

To provision the broader workload set, `dotnet workload install maui` is available. A full `dotnet build TodoApp.slnx` evaluates all configured target frameworks and requires the corresponding tooling/remote Apple setup. Prefer a single-platform build while learning. No MAUI workloads were installed automatically during this run.

## Database setup

**No database setup is needed for the current phase.** The domain entities are in-memory C# objects. There is no DbContext, SQLite file, connection string, EF tool, or migration yet. [DATABASE.md](docs/DATABASE.md) records the implemented domain rules and future storage decisions. PostgreSQL credentials will use development user secrets or `ConnectionStrings__TodoDatabase`; never commit real credentials.

## Learning path and Git

Start with [ARCHITECTURE.md](docs/ARCHITECTURE.md), then [LEARNINGS.md](docs/LEARNINGS.md). Read TaskItem alongside TaskItemTests to see how each business rule is checked. Follow [ROADMAP.md](docs/ROADMAP.md) after reviewing this phase. No later phase starts automatically.

This folder was not a Git repository at initial inspection. It is now initialized on `main`, with `origin` set to `https://github.com/DarryllPj77/Todo-app.git`. The foundation is recorded in commit `2b24e81` (`Create Todo App Phase 1 foundation`). See Git history and remote tracking status for subsequent documentation commits and publication status. No merge was performed.
