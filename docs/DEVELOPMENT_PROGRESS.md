# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 5 — MVVM task UI implemented; Windows build and native launch verified.** TaskListPage and TaskEditorPage consume SQLite through tested ViewModels, with CRUD, completion/reopening, filters, category/date/priority fields, confirmation, and error/busy states. The initial dark list/editor screens have been inspected. Full manual CRUD/restart acceptance, light/narrow layouts, and mobile targets remain pending; Phase 6 API task endpoints have not started.

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
- Replaced the welcome page with list/editor XAML, TaskRow/FilterOption display models, and TaskListViewModel/TaskEditorViewModel.
- Added ObservableViewModel and AsyncCommand for readable change notification, command state, exception handling, and duplicate-action prevention; no new application package was needed.
- Added per-window scoped navigation/DI, editor transients, native delete confirmation, draft fields/validation, and Save/Cancel/date-clear behavior.
- Added 16 presentation test cases using the actual portable MAUI source linked into the test project; native pages/navigation are checked by the separate Windows build.
- Added themed layouts and scripts/Run-Windows.ps1; fixed single-target builds with TodoAppTargetFrameworks rather than retargeting shared libraries.
- Installed a portable .NET 10.0.404 SDK plus maui-windows workload under ignored .tools/dotnet after the global installer was canceled during elevation. Local installation succeeded without administrator installation.
- Built and directly launched the unpackaged Windows app with bundled Windows App SDK files. Inspected native dark empty/editor screens; further UI input stopped when the user began using the editor.

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 0 warnings, 0 errors |
| `dotnet test TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 71 tests, 0 failed, 0 skipped; core/presentation sources rebuilt |
| `scripts/Run-Windows.ps1 -BuildOnly` | Passed: native Windows MAUI/XAML build, 0 warnings, 0 errors, using local SDK/workload |
| Initial global workload installation | Canceled during elevation and rolled back; local portable workload installation subsequently succeeded |
| Initial generic TargetFrameworks override after local setup | NETSDK1005 in shared libraries; fixed using the client-only TodoAppTargetFrameworks property |
| Full solution build (historical Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload |
| Windows native direct launch | Passed: task-list empty state and editor visible in dark mode; app left available for user testing |
| Full native CRUD/restart, light/narrow/mobile acceptance | Pending README manual checklist; not claimed from ViewModel/storage tests |
| SQLite native execution on Windows | Passed: temporary file reopen/round-trip/rollback tests; files cleaned up after each test |
| PostgreSQL verification | Not applicable; server persistence is not implemented |

Initial sandbox build attempts in Phase 1 returned no diagnostics; normal SDK access produced the core results. Phase 5 additionally compiled native Windows/XAML with a local workload. The API integration test verifies actual startup, routing, controller DI, status, and JSON; it does not test a deployed HTTPS listener/certificate. Presentation tests cover validation, unchanged saved state before Save/after Cancel, category reuse, due-date clearing, missing tasks, failed reads/writes, retry, delete confirmation, selection/filter/completion, double-save prevention, and failed post-save navigation. They do not simulate native clicks or theme switching.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions restored for net10.0. MVC.Testing 10.0.15 matches the installed ASP.NET runtime line. Microsoft.Data.Sqlite 10.0.15 supplies local SQLite/native dependencies; its synchronous native calls are offloaded once per operation rather than falsely treated as nonblocking ADO.NET async I/O. EF/Npgsql/JWT packages remain deferred.

## Work in progress / pending verification

- Complete the README native checklist: create/edit/cancel/toggle/filter/delete a disposable test task, close/reopen the app, and verify persistence.
- Check system light mode, narrow windows, accessibility, and mobile device targets. Only Windows has been compiled/launched in this run.
- Empty categories have no management/browser UI yet; typing a category name reuses one visible among stored tasks or creates a new category, rather than renaming it globally.
- Review Phase 5 before starting API task endpoints. No SQLite version-2 migration was introduced; future schema changes need explicit incremental migration tests.

## Uncommitted changes

Phase 5 changes are uncommitted; no commit or push has been performed for this phase. The working tree was clean at the start, with Phase 4 recorded in local commit `f191e39`. Use `git status` for live state. The portable SDK/workload, databases, template downloads, and build output remain ignored.

- New: MAUI list/editor pages and ViewModels, ObservableViewModel, AsyncCommand, TaskRow, FilterOption, ITaskNavigation/TaskNavigationService, TaskStyles.xaml, scripts/Run-Windows.ps1, and tests/TodoApp.Tests/TaskViewModelTests.cs.
- Removed: welcome page/ViewModel and the Models/Services .gitkeep placeholders.
- Modified: MAUI startup/DI/resources/project settings, test project source links, README, and all five docs.

After the user supplied the GitHub URL, the directory was initialized on `main` and `origin` was set to `https://github.com/DarryllPj77/Todo-app.git`. The remote reported no branches before the initial push. The user subsequently explicitly authorized committing and pushing the foundation.

Foundation file groups (historical):

- Root: `.gitignore`, `global.json`, `Directory.Build.props`, `TodoApp.slnx`, `TodoApp.Core.slnf`, `README.md`.
- Domain: `TodoApp.Domain.csproj`; reserved `Entities`, `Enums`, `Rules` folders with `.gitkeep`.
- Infrastructure: `TodoApp.Infrastructure.csproj`; reserved `Persistence`, `Repositories`, `Services` folders with `.gitkeep`.
- API: project file, `Program.cs`, `Controllers/HealthController.cs`, `.http` request, two appsettings files, launch settings; reserved `Services`, `Data`, `Configuration` folders.
- MAUI: project file, App XAML/code, MauiProgram, WelcomePage XAML/code, WelcomeViewModel, launch settings, platform files, and template resources; reserved `Models` and `Services` folders.
- Tests: project file and `ApiStartupTests.cs`.
- Docs: README plus ROADMAP, ARCHITECTURE, DATABASE, LEARNINGS, and this progress file.

Phase 5 preserves Domain, Infrastructure, and API behavior. `.tools`, `bin`, `obj`, and SQLite files/journals are local artifacts excluded from the intended Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- `00ee06a` — `docs: record initial foundation milestone`: foundation publication verified against GitHub on main in the preceding run.
- `0e50592` — `Implement Phase 2 domain models and business rules`: verified in local Git history at the start of Phase 3.
- `337e418` — `Implement Phase 3 local CRUD service and LINQ filters`: verified in local Git history at the start of Phase 4.
- `f191e39` — `Implement Phase 4 SQLite persistence`: verified in local Git history at the start of Phase 5.
- Phase 5 has no committed milestone yet. No merge or force-push has been performed.

## Next task

Run the Windows app with scripts/Run-Windows.ps1 and finish the README hands-on checklist. Then review Phase 5 before authorizing Phase 6 task REST endpoints/DTOs/validation. The current local app needs no API or login. Do not start Phase 6 automatically.

## What to learn now

Trace a XAML binding into TaskListViewModel or TaskEditorViewModel, then follow a command to ILocalTaskService. Learn change notification, observable collections, separate draft state, async command guards, and per-window DI scopes. Read the failed-save/double-save tests to see why edits are retained and saved creates are not repeated after navigation errors. See LEARNINGS for short examples.
