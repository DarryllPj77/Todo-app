# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 3 — Local CRUD and basic filters implemented.** ILocalTaskService and its temporary in-memory implementation support task creation, lookup/listing, full detail edits, completion/reopening, deletion, and five LINQ filters. The service is exercised by tests; it is not yet used by the MAUI welcome screen or API. Phase 1 client compilation and visual/runtime verification remain pending. Phase 4 SQLite persistence has not started.

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

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --nologo -m:1` | Passed: 0 warnings, 0 errors |
| `dotnet test TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 36 tests, 0 failed, 0 skipped; core projects rebuilt |
| `dotnet build TodoApp.slnx --nologo -m:1` (Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload; workload list is still empty in Phase 2 |
| MAUI launch, theme switching, native device verification | Not run; workloads are absent |
| PostgreSQL/SQLite verification | Not applicable; persistence is not implemented |

Initial sandbox build attempts returned no diagnostics; normal SDK access produced the definitive results above. NuGet restore succeeded for the core projects. The in-memory API integration test verifies actual startup, routing, controller DI, status, and JSON; it does not test a deployed HTTPS listener/certificate.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions restored for net10.0. MVC.Testing 10.0.15 matches the installed ASP.NET runtime line. EF/Npgsql/SQLite/JWT packages are intentionally deferred.

## Work in progress / pending verification

- Install a target MAUI workload and its native prerequisites using README instructions.
- Build/run that target; verify welcome content, resizing, and system light/dark theme changes.
- Review the Phase 3 service contract before implementing SQLite persistence. The temporary store is sequential-use process memory with live entity references, not durable/thread-safe storage. Full client compilation is not claimed.

## Uncommitted changes

Phase 3 changes are uncommitted; no commit or push has been performed for this phase. The working tree was clean at the start, with Phase 2 recorded in local commit `0e50592`. Use `git status` for live state. Local template downloads and build output remain ignored.

- New: `src/TodoApp.Domain/Enums/TaskFilter.cs`, `Services/TaskDetails.cs`, `Services/ILocalTaskService.cs`; `src/TodoApp.Infrastructure/Services/InMemoryLocalTaskService.cs`; `tests/TodoApp.Tests/LocalTaskServiceTests.cs`.
- Removed: `.gitkeep` from the now-populated Infrastructure/Services folder.
- Modified: README and all five documentation files to reflect the service contract, temporary limits, and next task.

After the user supplied the GitHub URL, the directory was initialized on `main` and `origin` was set to `https://github.com/DarryllPj77/Todo-app.git`. The remote reported no branches before the initial push. The user subsequently explicitly authorized committing and pushing the foundation.

Foundation file groups (historical):

- Root: `.gitignore`, `global.json`, `Directory.Build.props`, `TodoApp.slnx`, `TodoApp.Core.slnf`, `README.md`.
- Domain: `TodoApp.Domain.csproj`; reserved `Entities`, `Enums`, `Rules` folders with `.gitkeep`.
- Infrastructure: `TodoApp.Infrastructure.csproj`; reserved `Persistence`, `Repositories`, `Services` folders with `.gitkeep`.
- API: project file, `Program.cs`, `Controllers/HealthController.cs`, `.http` request, two appsettings files, launch settings; reserved `Services`, `Data`, `Configuration` folders.
- MAUI: project file, App XAML/code, MauiProgram, WelcomePage XAML/code, WelcomeViewModel, launch settings, platform files, and template resources; reserved `Models` and `Services` folders.
- Tests: project file and `ApiStartupTests.cs`.
- Docs: README plus ROADMAP, ARCHITECTURE, DATABASE, LEARNINGS, and this progress file.

Phase 3 preserves the existing entities, API, and MAUI source. `.tools`, `bin`, and `obj` are local generated artifacts excluded from the intended Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- `00ee06a` — `docs: record initial foundation milestone`: foundation publication verified against GitHub on main in the preceding run.
- `0e50592` — `Implement Phase 2 domain models and business rules`: verified in local Git history at the start of Phase 3.
- Phase 3 has no committed milestone yet. No merge or force-push has been performed.

## Next task

Review Phase 3, then implement ILocalTaskService with SQLite in Phase 4: choose a compatible library, define loading/identity and category mapping, persist under app data, plan schema upgrades, and test restart retention. Before client UI work, finish the MAUI workload setup and outstanding Phase 1 client verification. The task UI remains Phase 5. Do not proceed automatically to either phase.

## What to learn now

Read ILocalTaskService, then trace its implementation alongside LocalTaskServiceTests. Learn how interfaces separate a caller from storage, how LINQ filters and materializes a sequence, why Task.FromResult is appropriate for memory-only work, and how cancellation is observed. Compare local-day versus UTC-day filtering and distinguish read-only collection membership from immutable entity objects. See LEARNINGS for short examples.
