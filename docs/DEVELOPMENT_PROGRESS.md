# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 1 — Solution foundation.** Source scaffolding is implemented; client compilation and visual/runtime verification await MAUI tooling. Phase 2 has not started. No domain model was required for this foundation to compile, so none was introduced.

## Completed work

- Inspected an empty workspace: no pre-existing source, Git repository, or applicable ancestor AGENTS.md found.
- Verified SDK 10.0.404 and .NET runtime 10.0.15; no workloads installed.
- Created Domain, Infrastructure, API, MAUI, and Tests projects with explicit references.
- Used Microsoft's MAUI 10.0.130 template through an isolated, ignored `.tools` template hive; kept Android/iOS/Mac Catalyst/Windows startup resources and manifests.
- Replaced the template counter/Shell with a DI-created welcome page and immutable ViewModel; retained template fonts, icons, and theme styles.
- Added API controller registration, Problem Details, exception handling, HTTPS redirection, and liveness controller. Removed the generated weather example.
- Added one meaningful API startup integration test and the core solution filter.
- Added all six requested Markdown documents and pinned SDK configuration.

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --nologo -m:1` | Passed: 0 warnings, 0 errors |
| `dotnet test TodoApp.Core.slnf --no-build --no-restore --nologo -m:1` | Passed: 1 test, 0 failed, 0 skipped |
| `dotnet build TodoApp.slnx --nologo -m:1` | Blocked: NETSDK1147, missing maui-android workload |
| MAUI launch, theme switching, native device verification | Not run; workloads are absent |
| PostgreSQL/SQLite verification | Not applicable; persistence is not implemented |

Initial sandbox build attempts returned no diagnostics; normal SDK access produced the definitive results above. NuGet restore succeeded for the core projects. The in-memory API integration test verifies actual startup, routing, controller DI, status, and JSON; it does not test a deployed HTTPS listener/certificate.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions restored for net10.0. MVC.Testing 10.0.15 matches the installed ASP.NET runtime line. EF/Npgsql/SQLite/JWT packages are intentionally deferred.

## Work in progress / pending verification

- Install a target MAUI workload and its native prerequisites using README instructions.
- Build/run that target; verify welcome content, resizing, and system light/dark theme changes.
- Review Phase 1 before approving Phase 2. Full client compilation is not claimed.

## Uncommitted changes

The foundation files are committed in `2b24e81`. This documentation follow-up is being prepared for a separate commit as part of the user's authorized push. Use `git status` for current uncommitted changes rather than treating this publication-time snapshot as live Git status. Local template downloads and build output remain ignored.

After the user supplied the GitHub URL, the directory was initialized on `main` and `origin` was set to `https://github.com/DarryllPj77/Todo-app.git`. The remote reported no branches before the initial push. The user subsequently explicitly authorized committing and pushing the foundation.

Created file groups:

- Root: `.gitignore`, `global.json`, `Directory.Build.props`, `TodoApp.slnx`, `TodoApp.Core.slnf`, `README.md`.
- Domain: `TodoApp.Domain.csproj`; reserved `Entities`, `Enums`, `Rules` folders with `.gitkeep`.
- Infrastructure: `TodoApp.Infrastructure.csproj`; reserved `Persistence`, `Repositories`, `Services` folders with `.gitkeep`.
- API: project file, `Program.cs`, `Controllers/HealthController.cs`, `.http` request, two appsettings files, launch settings; reserved `Services`, `Data`, `Configuration` folders.
- MAUI: project file, App XAML/code, MauiProgram, WelcomePage XAML/code, WelcomeViewModel, launch settings, platform files, and template resources; reserved `Models` and `Services` folders.
- Tests: project file and `ApiStartupTests.cs`.
- Docs: README plus ROADMAP, ARCHITECTURE, DATABASE, LEARNINGS, and this progress file.

Modified pre-existing files: **none**. Generated starter files were adjusted only after inspection. `.tools`, `bin`, and `obj` are local generated artifacts excluded from the intended Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- A documentation follow-up records this milestone. Publication is verified against GitHub after the push; inspect `git log` and `git status -sb` for the current commit and tracking state. No merge or force-push is planned.

## Next task

First finish MAUI workload setup and Phase 1 client verification. Then, after user approval, define TaskItem/Category/Priority and readable validation/completion rules with focused tests in Phase 2. Do not begin CRUD, persistence, synchronization, or advanced features during this run.

## What to learn now

Trace project references, then follow API startup to its controller and MAUI startup to its ViewModel binding. Understand why the UI, domain rules, and storage have different homes, how constructors receive dependencies, and why a partial build cannot prove the whole application works. See LEARNINGS for short examples.
