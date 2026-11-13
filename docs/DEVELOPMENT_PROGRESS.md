# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 4 — SQLite persistence implemented and file-tested on Windows.** SqliteLocalTaskService implements the existing local contract with task/category tables, schema version 1, parameterized SQL, and transactional writes. MAUI registers it using its app-data path, but the welcome page does not consume it yet. Phase 1/4 MAUI compilation, native packaging, and device verification remain pending because no workloads are installed. Phase 5 task screens have not started.

## Completed work

- Inspected an empty workspace: no pre-existing source, Git repository, or applicable ancestor AGENTS.md found.
- Verified SDK 10.0.404 and .NET runtime 10.0.15; no workloads installed.
- Created Domain, Infrastructure, API, MAUI, and Tests projects with explicit references.
- Used Microsoft's MAUI 10.0.130 template through an isolated, ignored `.tools` template hive; kept Android/iOS/Mac Catalyst/Windows startup resources and manifests.
- Replaced the template counter/Shell with a DI-created welcome page and immutable ViewModel; retained template fonts, icons, and theme styles.
- Added API controller registration, Problem Details, exception handling, HTTPS redirection, and liveness controller. Removed the generated weather example.
- Added one meaningful API startup integration test and the core solution filter.
- Added all six requested Markdown documents and pinned SDK configuration.
- Added TaskItem and Category entities plus TaskPriority (Low/Normal/High); no new packages were needed.
- Added trimmed text validation, optional description/due date, UTC timestamps, completion/reopening, and category assignment that keeps both navigation directions consistent.
- Validated all edit inputs before mutation; repeated equivalent updates preserve UpdatedAt.
- Added 21 domain test cases; updated README, architecture, database rules, roadmap, learnings, and this progress record.
- Added TaskFilter, TaskDetails, and ILocalTaskService in Domain plus InMemoryLocalTaskService in Infrastructure, without new package dependencies.
- Added full editable-field replacement, null/false missing-task results, domain validation before mutation, task/category cleanup on deletion, and cancellation checks.
- Implemented All/Pending/Completed/Due Today/High Priority using LINQ, with case-insensitive ordinal title sorting and ID tie-breaking. Due Today/High Priority include completed tasks.
- Added 14 service test cases covering CRUD, failed writes, missing tasks, optional-field clearing, filters, local time-zone/midnight behavior, materialized result membership, cancellation, and invalid inputs.
- Added Microsoft.Data.Sqlite 10.0.15, checked against the package's published compatibility and successfully restored against the installed .NET 10 SDK.
- Added versioned SQLite initialization, task/category FK mapping, parameterized SQL, worker-thread execution, and per-operation connections/transactions.
- Added internal hydration factories to preserve stored identity/timestamps and shared TaskQueries to keep filter/order behavior identical across implementations.
- Registered SQLite in MAUI at FileSystem.AppDataDirectory/todoapp.db3; added Git ignores for database/journal files.
- Added 19 real-file integration test cases for reopen retention, detached objects, full CRUD/filter behavior, schema/version safety, forced-write rollback, FK behavior, cancellation, parameter handling, and separate-instance concurrent writes.

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 0 warnings, 0 errors |
| `dotnet test TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 55 tests, 0 failed, 0 skipped; core projects rebuilt |
| Windows-only MAUI build with `-p:TargetFrameworks=net10.0-windows10.0.19041.0` | Blocked: NETSDK1147; no workloads installed (resolver names maui-tizen in this environment, despite a Windows-only target; the app has no Tizen target) |
| Full solution build (historical Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload |
| MAUI launch, theme switching, native device verification | Not run; workloads are absent |
| SQLite native execution on Windows | Passed: temporary file reopen/round-trip/rollback tests; files cleaned up after each test |
| PostgreSQL verification | Not applicable; server persistence is not implemented |

Initial sandbox build attempts returned no diagnostics; normal SDK access produced the definitive results above. NuGet restore succeeded for the core projects. The in-memory API integration test verifies actual startup, routing, controller DI, status, and JSON; it does not test a deployed HTTPS listener/certificate.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions restored for net10.0. MVC.Testing 10.0.15 matches the installed ASP.NET runtime line. Microsoft.Data.Sqlite 10.0.15 supplies local SQLite/native dependencies; its synchronous native calls are offloaded once per operation rather than falsely treated as nonblocking ADO.NET async I/O. EF/Npgsql/JWT packages remain deferred.

## Work in progress / pending verification

- Install a target MAUI workload and its native prerequisites using README instructions.
- Build/run that target; verify welcome content, resizing, and system light/dark theme changes.
- Verify native SQLite packaging and the app-data path on the chosen MAUI target after workload setup. The persistent service has passed Windows test-host checks only; client compilation is not claimed.
- Review Phase 4 before adding Phase 5 task screens. No SQLite version-2 migration is needed yet; future schema changes require explicit incremental migration tests.

## Uncommitted changes

Phase 4 changes are uncommitted; no commit or push has been performed for this phase. The working tree was clean at the start, with Phase 3 recorded in local commit `337e418`. Use `git status` for live state. Local database files, template downloads, and build output remain ignored.

- New: Domain/Properties/AssemblyInfo.cs, Domain/Rules/TaskQueries.cs, Infrastructure/Persistence/SqliteTaskDatabase.cs, Infrastructure/Services/SqliteLocalTaskService.cs, and tests/TodoApp.Tests/SqliteLocalTaskServiceTests.cs.
- Removed: `.gitkeep` from now-populated Domain/Rules and Infrastructure/Persistence.
- Modified: TaskItem/Category (internal restoration), ILocalTaskService comments, the in-memory filter implementation, Infrastructure project package reference, MauiProgram DI, .gitignore, README, and all five docs.

After the user supplied the GitHub URL, the directory was initialized on `main` and `origin` was set to `https://github.com/DarryllPj77/Todo-app.git`. The remote reported no branches before the initial push. The user subsequently explicitly authorized committing and pushing the foundation.

Foundation file groups (historical):

- Root: `.gitignore`, `global.json`, `Directory.Build.props`, `TodoApp.slnx`, `TodoApp.Core.slnf`, `README.md`.
- Domain: `TodoApp.Domain.csproj`; reserved `Entities`, `Enums`, `Rules` folders with `.gitkeep`.
- Infrastructure: `TodoApp.Infrastructure.csproj`; reserved `Persistence`, `Repositories`, `Services` folders with `.gitkeep`.
- API: project file, `Program.cs`, `Controllers/HealthController.cs`, `.http` request, two appsettings files, launch settings; reserved `Services`, `Data`, `Configuration` folders.
- MAUI: project file, App XAML/code, MauiProgram, WelcomePage XAML/code, WelcomeViewModel, launch settings, platform files, and template resources; reserved `Models` and `Services` folders.
- Tests: project file and `ApiStartupTests.cs`.
- Docs: README plus ROADMAP, ARCHITECTURE, DATABASE, LEARNINGS, and this progress file.

Phase 4 preserves the API and existing MAUI welcome screen. `.tools`, `bin`, `obj`, and SQLite files/journals are local artifacts excluded from the intended Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- `00ee06a` — `docs: record initial foundation milestone`: foundation publication verified against GitHub on main in the preceding run.
- `0e50592` — `Implement Phase 2 domain models and business rules`: verified in local Git history at the start of Phase 3.
- `337e418` — `Implement Phase 3 local CRUD service and LINQ filters`: verified in local Git history at the start of Phase 4.
- Phase 4 has no committed milestone yet. No merge or force-push has been performed.

## Next task

Review Phase 4, finish target MAUI tooling setup, then build Phase 5: task-list/editor ViewModels, observable collections and commands, create/edit/delete/toggle/filter controls, category input, empty/error states, and responsive light/dark screens. Verify actual app restart retention and native behavior on the target device; file reopen tests do not replace that check. Do not begin Phase 5 automatically.

## What to learn now

Read SqliteTaskDatabase's initialization and SqliteLocalTaskService's create/read/update paths alongside SqliteLocalTaskServiceTests. Learn parameterized SQL, connection/transaction disposal, rollback, foreign keys, schema versions, detached hydration, and invariant date formats. Compare the provider's synchronous I/O with the task-returning service boundary, and understand why cancellation is cooperative. See LEARNINGS for short examples.
