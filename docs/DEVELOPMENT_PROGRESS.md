# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 2 — Domain models and business rules implemented.** The user authorized starting the next system-building step after the foundation push. TaskItem, Category, and TaskPriority are implemented with validation and focused tests. Phase 1 client compilation and visual/runtime verification remain pending: no MAUI workloads are installed. Phase 3 CRUD/filtering has not started.

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

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --nologo -m:1` | Passed: 0 warnings, 0 errors |
| `dotnet test TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 22 tests, 0 failed, 0 skipped; core projects rebuilt |
| `dotnet build TodoApp.slnx --nologo -m:1` (Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload; workload list is still empty in Phase 2 |
| MAUI launch, theme switching, native device verification | Not run; workloads are absent |
| PostgreSQL/SQLite verification | Not applicable; persistence is not implemented |

Initial sandbox build attempts returned no diagnostics; normal SDK access produced the definitive results above. NuGet restore succeeded for the core projects. The in-memory API integration test verifies actual startup, routing, controller DI, status, and JSON; it does not test a deployed HTTPS listener/certificate.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions restored for net10.0. MVC.Testing 10.0.15 matches the installed ASP.NET runtime line. EF/Npgsql/SQLite/JWT packages are intentionally deferred.

## Work in progress / pending verification

- Install a target MAUI workload and its native prerequisites using README instructions.
- Build/run that target; verify welcome content, resizing, and system light/dark theme changes.
- Review the Phase 2 domain rules before proceeding to CRUD/filtering. Full client compilation is not claimed.

## Uncommitted changes

The Phase 2 changes are uncommitted. No commit or push has been performed for this phase. Use `git status` for live state. Local template downloads and build output remain ignored.

- New: `src/TodoApp.Domain/Entities/TaskItem.cs`, `Entities/Category.cs`, `Enums/TaskPriority.cs`; `tests/TodoApp.Tests/TaskItemTests.cs` and `CategoryTests.cs`.
- Removed: `.gitkeep` from the now-populated Domain/Entities and Domain/Enums folders.
- Modified: README and all five documentation files to reflect the implemented domain behavior and next task.

After the user supplied the GitHub URL, the directory was initialized on `main` and `origin` was set to `https://github.com/DarryllPj77/Todo-app.git`. The remote reported no branches before the initial push. The user subsequently explicitly authorized committing and pushing the foundation.

Foundation file groups (historical):

- Root: `.gitignore`, `global.json`, `Directory.Build.props`, `TodoApp.slnx`, `TodoApp.Core.slnf`, `README.md`.
- Domain: `TodoApp.Domain.csproj`; reserved `Entities`, `Enums`, `Rules` folders with `.gitkeep`.
- Infrastructure: `TodoApp.Infrastructure.csproj`; reserved `Persistence`, `Repositories`, `Services` folders with `.gitkeep`.
- API: project file, `Program.cs`, `Controllers/HealthController.cs`, `.http` request, two appsettings files, launch settings; reserved `Services`, `Data`, `Configuration` folders.
- MAUI: project file, App XAML/code, MauiProgram, WelcomePage XAML/code, WelcomeViewModel, launch settings, platform files, and template resources; reserved `Models` and `Services` folders.
- Tests: project file and `ApiStartupTests.cs`.
- Docs: README plus ROADMAP, ARCHITECTURE, DATABASE, LEARNINGS, and this progress file.

Phase 2 preserves the existing API, Infrastructure, and MAUI source. `.tools`, `bin`, and `obj` are local generated artifacts excluded from the intended Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- `00ee06a` — `docs: record initial foundation milestone`: foundation publication verified against GitHub on main in the preceding run.
- Phase 2 has no committed milestone yet. No merge or force-push has been performed.

## Next task

Review Phase 2, then add the small local task service and the five LINQ filters in Phase 3. Before client UI work, finish the MAUI workload setup and outstanding Phase 1 client verification. Persistence remains Phase 4, and the task UI remains Phase 5; neither is implemented in this step.

## What to learn now

Read TaskItem's constructor, UpdateDetails, SetCompleted, and SetCategory alongside their tests. Practice why private setters protect invariants, why an enum still needs validation, and how a rejected edit avoids partial mutation. Compare DateOnly deadlines with UTC DateTimeOffset timestamps, then trace both sides of the Category relationship. See LEARNINGS for short examples.
