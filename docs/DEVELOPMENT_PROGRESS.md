# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 7 — PostgreSQL/EF Core persistence implemented and verified against PostgreSQL 18.** The normal API uses PostgresTaskService and a request-scoped TodoDbContext with generated InitialCreate migration. All 113 tests passed with an isolated test connection, including nine real PostgreSQL cases. Windows-targeted solution build passed with zero warnings/errors. Your development role/database/connection still needs local setup; existing PostgreSQL databases were not modified. Authentication/user ownership remains Phase 8.

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
- Added TasksController, TaskWriteRequest, TaskResponse, and a singleton InMemoryTaskService with locked request-safe state and flat response snapshots.
- Added normalized input validation, unknown JSON member rejection, string-only priority JSON, server-owned IDs/timestamps, full PUT replacement, and category-name reuse.
- Implemented 200/201/204 success responses, Location creation URLs, 400 ValidationProblemDetails, and 404 missing-task responses.
- Added Development-only startup enforcement and request cancellation propagation; no database/JWT packages were added.
- Added 32 API test cases and sample GET/POST/PUT/DELETE requests in TodoApp.Api.http. A real loopback CRUD smoke test passed and its temporary listener was stopped.
- Added EF Core/Relational/Design 10.0.15, Npgsql EF provider 10.0.3, and a repository-local dotnet-ef 10.0.15 manifest. Explicit Relational pinning resolved an initial provider/Design patch assembly mismatch.
- Added TodoDbContext, Fluent entity configurations, generated InitialCreate migration/designer/snapshot, indexed category FK, check constraints, normalized category uniqueness, and UTC microsecond timestamp persistence.
- Added scalar CategoryId/private EF materialization support without domain framework dependencies. Existing SQLite schema and restoration remain unchanged; scalar clearing works even without loaded navigation.
- Introduced ITaskService with scoped PostgresTaskService as the normal API registration. The old memory implementation is only an explicit portable-test substitute. Added user-secrets ID and configuration-only connection loading with a tested missing-connection startup error.
- Added nine isolated PostgreSQL tests and one configuration test; added scripts/Test-Postgres.ps1. Tests cover migrated schema consistency, persistent REST round trips across new hosts, category concurrency, FK deletion/clearing, rollback, validation, filters, and uniqueness/length constraints.
- Verified and stopped an isolated loopback-only PostgreSQL test cluster under .tools/pg-tests. All generated test databases were removed; the installed PostgreSQL 18 Windows service remains running and unchanged.

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 0 warnings, 0 errors |
| `scripts/Test-Postgres.ps1` with TODOAPP_TEST_POSTGRES set to the isolated cluster | Passed: 113 tests, 0 failed, 0 skipped |
| Windows-targeted `TodoApp.slnx` build using local SDK and TodoAppTargetFrameworks | Passed: all five projects, 0 warnings, 0 errors |
| Default portable test mode without TODOAPP_TEST_POSTGRES | Nine PostgreSQL facts are explicitly skipped; portable contract tests use a test-only memory substitute |
| Live HTTP CRUD on temporary loopback port 5073 | Passed: POST 201, GET/PUT 200, DELETE 204, final GET []; listener stopped |
| `scripts/Run-Windows.ps1 -BuildOnly` (Phase 5 check) | Passed: native Windows MAUI/XAML build, 0 warnings, 0 errors; MAUI source unchanged in Phase 6 |
| Initial global workload installation | Canceled during elevation and rolled back; local portable workload installation subsequently succeeded |
| Initial generic TargetFrameworks override after local setup | NETSDK1005 in shared libraries; fixed using the client-only TodoAppTargetFrameworks property |
| Full solution build (historical Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload |
| Windows native direct launch | Passed: task-list empty state and editor visible in dark mode; app left available for user testing |
| Full native CRUD/restart, light/narrow/mobile acceptance | Pending README manual checklist; not claimed from ViewModel/storage tests |
| SQLite native execution on Windows | Passed: temporary file reopen/round-trip/rollback tests; files cleaned up after each test |
| PostgreSQL 18 verification | Passed: nine real-database cases, each migrating/cleaning up a unique database; zero test databases remained before isolated cluster shutdown |

Initial sandbox build attempts in Phase 1 returned no diagnostics; normal SDK access produced the core results. Phase 5 compiled native Windows/XAML with a local workload. Phase 6 added 32 tests for the actual API pipeline: routes, response contracts, missing IDs, malformed JSON/values, length limits, unknown/server-owned fields, all filters, category reuse, parallel creates, cancellation, fresh server state, and refusal to start in Production. Integration tests use an in-memory HTTPS request host, not a deployed TLS certificate. A separate loopback HTTP listener passed real socket CRUD checks. The HTTP-only profile logged its expected missing HTTPS redirect-port warning. An initial smoke assertion mishandled PowerShell's empty-array wrapping; checking the final JSON body directly verified it was []. No application change was needed for that assertion correction.

Presentation tests retain their Phase 5 coverage of validation, draft isolation, cancel, retry, confirmations, missing tasks, and double-save/navigation failures. They do not simulate native clicks or theme switching.

In Phase 7, the running Phase 6 API initially locked normal build outputs. Tests and migration generation proceeded in isolated .tools/phase7-build output without stopping it. The user then reported stopping the API; the final Windows-targeted solution build used normal output successfully. Test factory configuration was moved to validation after host configuration was built, allowing an explicit test substitute while normal startup still rejects a missing connection. Final database tests used scripts/Test-Postgres.ps1 and did not touch the installed service's databases.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions restored for net10.0. MVC.Testing 10.0.15 matches the installed ASP.NET runtime line. Microsoft.Data.Sqlite 10.0.15 supplies local SQLite/native dependencies. EF Core/Relational/Design/tool 10.0.15 are aligned; Npgsql EF 10.0.3 supports that EF 10 version. JWT/authentication remains deferred.

## Work in progress / pending verification

- Complete the README native checklist: create/edit/cancel/toggle/filter/delete a disposable test task, close/reopen the app, and verify persistence.
- Check system light mode, narrow windows, accessibility, and mobile device targets. Only Windows has been compiled/launched in this run.
- Empty categories have no management/browser UI yet; typing a category name reuses one visible among stored tasks or creates a new category, rather than renaming it globally.
- Follow DATABASE.md to create your development role/database, set ConnectionStrings:TodoDatabase in user secrets/environment configuration, and apply the included migration. No credentials or persistent app connection were invented during this run.
- Review Phase 7 before authentication/ownership. API data is now durable in PostgreSQL once configured, but there is still no client synchronization. No SQLite schema change was introduced.
- API DueToday currently follows the server's local date; define a per-user time-zone policy before multi-user integration. Authentication/ownership and production deployment remain later work.

## Uncommitted changes

Phase 7 changes are uncommitted; no commit or push has been performed for this phase. The working tree was clean at the start, with Phase 6 recorded in local commit `9935192`. Use git status for live state. Portable tools, test-cluster files, and build output remain ignored.

- New: Infrastructure/Persistence/Server context/configurations/migrations, API/Configuration/DatabaseConfiguration.cs, ITaskService/PostgresTaskService, dotnet-tools.json, scripts/Test-Postgres.ps1, and ApiTestFactory/PostgresFactAttribute/PostgresPersistenceTests.
- Removed: API/Configuration .gitkeep placeholder.
- Modified: TaskItem/Category EF materialization/FK support, Infrastructure/API package project files, API startup/controller/service interface registration, existing API tests, README, and all five docs.

After the user supplied the GitHub URL, the directory was initialized on `main` and `origin` was set to `https://github.com/DarryllPj77/Todo-app.git`. The remote reported no branches before the initial push. The user subsequently explicitly authorized committing and pushing the foundation.

Foundation file groups (historical):

- Root: `.gitignore`, `global.json`, `Directory.Build.props`, `TodoApp.slnx`, `TodoApp.Core.slnf`, `README.md`.
- Domain: `TodoApp.Domain.csproj`; reserved `Entities`, `Enums`, `Rules` folders with `.gitkeep`.
- Infrastructure: `TodoApp.Infrastructure.csproj`; reserved `Persistence`, `Repositories`, `Services` folders with `.gitkeep`.
- API: project file, `Program.cs`, `Controllers/HealthController.cs`, `.http` request, two appsettings files, launch settings; reserved `Services`, `Data`, `Configuration` folders.
- MAUI: project file, App XAML/code, MauiProgram, WelcomePage XAML/code, WelcomeViewModel, launch settings, platform files, and template resources; reserved `Models` and `Services` folders.
- Tests: project file and `ApiStartupTests.cs`.
- Docs: README plus ROADMAP, ARCHITECTURE, DATABASE, LEARNINGS, and this progress file.

Phase 7 preserves the public REST DTO/route contract and MAUI screen behavior. Shared domain persistence details changed and passed SQLite/presentation regression tests plus the Windows build. .tools, bin, obj, and SQLite files/journals remain excluded from Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- `00ee06a` — `docs: record initial foundation milestone`: foundation publication verified against GitHub on main in the preceding run.
- `0e50592` — `Implement Phase 2 domain models and business rules`: verified in local Git history at the start of Phase 3.
- `337e418` — `Implement Phase 3 local CRUD service and LINQ filters`: verified in local Git history at the start of Phase 4.
- `f191e39` — `Implement Phase 4 SQLite persistence`: verified in local Git history at the start of Phase 5.
- `7639dff` — `Implement Phase 5 offline MAUI task interface`: verified in local Git history at the start of Phase 6.
- `9935192` — `Implement Phase 6 task REST API`: verified in local Git history at the start of Phase 7.
- Phase 7 has no committed milestone yet. No merge or force-push has been performed.

## Next task

Configure the development database and apply InitialCreate using DATABASE.md, then review Phase 7. The next implementation phase is Phase 8 authentication/ownership before client API integration or synchronization. Finish the independent MAUI manual checklist as needed. Do not begin authentication or synchronization automatically.

## What to learn now

Trace TasksController through ITaskService/PostgresTaskService into TodoDbContext. Learn scoped contexts, tracking/no-tracking, Fluent mappings, FK materialization, migrations/model snapshots, database constraints/upserts, transaction rollback, and secret-based configuration. Compare PostgreSQL native async operations with the SQLite adapter and understand timestamp precision. See LEARNINGS for short examples.
