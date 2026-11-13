# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 8 — Server authentication and ownership implemented.** Register/login use framework password hashing and short-lived JWTs; all task/category operations are owner-scoped. AddAuthenticationOwnership preserves legacy anonymous rows without assigning them. Verification ran 127 tests, including 18 real PostgreSQL facts, with no skips. Windows-targeted solution builds with zero warnings/errors. MAUI has a registered SecureStorage wrapper but remains offline; client login/API integration is Phase 9. Your local API needs the database/signing-secret/migration setup in DATABASE.md.

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
- Added UserAccount persistence, unique normalized email, framework password hashing/rehash, strict auth request DTOs, register/login/me endpoints, and generic login failures.
- Added JWT bearer 10.0.15 with validated configured signing key, HS256/issuer/audience/lifetime checks, short expiry, and account-existence validation. Auth endpoints share a process-local per-IP limiter.
- Added claim-derived current-user context, Authorize on task endpoints, owner filtering on every server task operation, and per-user category upserts/uniqueness.
- Generated AddAuthenticationOwnership migration/designer/snapshot. Legacy anonymous rows retain null owners and remain hidden rather than being deleted or claimed by new accounts.
- Added MAUI SecureTokenStore using SecureStorage, with no password/plaintext fallback; client login is not wired yet.
- Added nine real PostgreSQL authentication/isolation facts and five portable settings cases. Existing tests use explicit fake auth only in contract/persistence fixtures; actual JWT tests use real middleware.
- Updated .http samples to placeholder credentials/private bearer-token variables and added signing-key generation/migration instructions.

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 0 warnings, 0 errors |
| `scripts/Test-Postgres.ps1` with TODOAPP_TEST_POSTGRES set to the isolated cluster | Passed: 127 tests, 0 failed, 0 skipped |
| Windows-targeted `TodoApp.slnx` build using local SDK and TodoAppTargetFrameworks | Passed: all five projects, 0 warnings, 0 errors |
| Default portable test mode without TODOAPP_TEST_POSTGRES | Eighteen PostgreSQL facts are explicitly skipped; portable REST tests use explicit memory/auth substitutes |
| Live HTTP CRUD on temporary loopback port 5073 | Passed: POST 201, GET/PUT 200, DELETE 204, final GET []; listener stopped |
| `scripts/Run-Windows.ps1 -BuildOnly` (Phase 5 check) | Passed: native Windows MAUI/XAML build, 0 warnings, 0 errors; MAUI source unchanged in Phase 6 |
| Initial global workload installation | Canceled during elevation and rolled back; local portable workload installation subsequently succeeded |
| Initial generic TargetFrameworks override after local setup | NETSDK1005 in shared libraries; fixed using the client-only TodoAppTargetFrameworks property |
| Full solution build (historical Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload |
| Windows native direct launch | Passed: task-list empty state and editor visible in dark mode; app left available for user testing |
| Full native CRUD/restart, light/narrow/mobile acceptance | Pending README manual checklist; not claimed from ViewModel/storage tests |
| SQLite native execution on Windows | Passed: temporary file reopen/round-trip/rollback tests; files cleaned up after each test |
| PostgreSQL 18 verification | Passed: 18 real-database facts, including real auth/JWT/isolation and legacy-migration checks; each fixture cleans up its own database |

Initial sandbox build attempts in Phase 1 returned no diagnostics; normal SDK access produced the core results. Phase 5 compiled native Windows/XAML with a local workload. Phase 6 added 32 tests for the actual API pipeline: routes, response contracts, missing IDs, malformed JSON/values, length limits, unknown/server-owned fields, all filters, category reuse, parallel creates, cancellation, fresh server state, and refusal to start in Production. Integration tests use an in-memory HTTPS request host, not a deployed TLS certificate. A separate loopback HTTP listener passed real socket CRUD checks. The HTTP-only profile logged its expected missing HTTPS redirect-port warning. An initial smoke assertion mishandled PowerShell's empty-array wrapping; checking the final JSON body directly verified it was []. No application change was needed for that assertion correction.

Presentation tests retain their Phase 5 coverage of validation, draft isolation, cancel, retry, confirmations, missing tasks, and double-save/navigation failures. They do not simulate native clicks or theme switching.

In Phase 7, the running Phase 6 API initially locked normal build outputs. Tests and migration generation proceeded in isolated .tools/phase7-build output without stopping it. The user then reported stopping the API; the final Windows-targeted solution build used normal output successfully. Test factory configuration was moved to validation after host configuration was built, allowing an explicit test substitute while normal startup still rejects a missing connection. Final database tests used scripts/Test-Postgres.ps1 and did not touch the installed service's databases.

Phase 8 tested hashed registration/login, unique email, password whitespace preservation, generic credential failure, auth rate limiting, missing/invalid/expired/unsigned/wrong-key/issuer/audience tokens, unknown/deleted accounts, every task route's authentication/ownership behavior, per-user categories, spoofed owner input, and migration preservation of legacy rows. No real signing secret/password/token was committed or printed. Native secure token round trips, login UI, refresh tokens, password reset/email verification, and production identity/deployment acceptance are not claimed.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions. MVC.Testing, JwtBearer, EF Core/Relational/Design/tool are aligned at 10.0.15; Npgsql EF 10.0.3 supports the EF 10 version. Microsoft.Data.Sqlite remains 10.0.15. Password hashing/rate limiting come from ASP.NET Core, and SecureStorage from MAUI.

## Work in progress / pending verification

- Complete the README native checklist: create/edit/cancel/toggle/filter/delete a disposable test task, close/reopen the app, and verify persistence.
- Check system light mode, narrow windows, accessibility, and mobile device targets. Only Windows has been compiled/launched in this run.
- Empty categories have no management/browser UI yet; typing a category name reuses one visible among stored tasks or creates a new category, rather than renaming it globally.
- Follow DATABASE.md to set your development connection and a cryptographically random Base64 Jwt:SigningKey, then apply both migrations. No user credentials or persistent app connection were invented during this run.
- Legacy anonymous tasks/categories remain unowned and hidden. Any reassignment needs explicit administrative review; registration does not claim old data.
- Review Phase 8 before client integration. No refresh token/revocation/password-reset/email-verification system is implemented, and the API remains Development-only pending hardening/acceptance.
- SecureTokenStore compiles on Windows; actual device storage/acquisition/expiry/logout UI checks belong to Phase 9. No SQLite schema change or client login was introduced.
- API DueToday currently follows the server's local date; define a per-user time-zone policy before multi-user integration. Authentication/ownership and production deployment remain later work.

## Uncommitted changes

Phase 8 changes are uncommitted; no commit or push has been performed for this phase. The working tree was clean at the start, with Phase 7 recorded in local commit 5810242. Portable tools, test-cluster files, and build output remain ignored.

- New: UserAccount, AddAuthenticationOwnership migration/designer, AuthenticationConfiguration/JwtSettings, AuthRequests/AuthController/AuthService/JwtTokenService/ICurrentUser, SecureTokenStore, TestAuthentication/AuthenticationSettingsTests/PostgresAuthenticationTests.
- Modified: server EF mappings/snapshot, PostgresTaskService owner scoping, TasksController/startup/API project/appsettings/HTTP samples, MAUI DI, existing test factories/persistence cases, README, and all five docs.
- Removed files: none.

After the user supplied the GitHub URL, the directory was initialized on `main` and `origin` was set to `https://github.com/DarryllPj77/Todo-app.git`. The remote reported no branches before the initial push. The user subsequently explicitly authorized committing and pushing the foundation.

Foundation file groups (historical):

- Root: `.gitignore`, `global.json`, `Directory.Build.props`, `TodoApp.slnx`, `TodoApp.Core.slnf`, `README.md`.
- Domain: `TodoApp.Domain.csproj`; reserved `Entities`, `Enums`, `Rules` folders with `.gitkeep`.
- Infrastructure: `TodoApp.Infrastructure.csproj`; reserved `Persistence`, `Repositories`, `Services` folders with `.gitkeep`.
- API: project file, `Program.cs`, `Controllers/HealthController.cs`, `.http` request, two appsettings files, launch settings; reserved `Services`, `Data`, `Configuration` folders.
- MAUI: project file, App XAML/code, MauiProgram, WelcomePage XAML/code, WelcomeViewModel, launch settings, platform files, and template resources; reserved `Models` and `Services` folders.
- Tests: project file and `ApiStartupTests.cs`.
- Docs: README plus ROADMAP, ARCHITECTURE, DATABASE, LEARNINGS, and this progress file.

Phase 8 preserves task editable/response DTOs and MAUI offline screen behavior, while task routes now require bearer authentication and enforce ownership. Shared Domain/SQLite source/schema remain unchanged. .tools, bin, obj, and SQLite files/journals remain excluded from Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- `00ee06a` — `docs: record initial foundation milestone`: foundation publication verified against GitHub on main in the preceding run.
- `0e50592` — `Implement Phase 2 domain models and business rules`: verified in local Git history at the start of Phase 3.
- `337e418` — `Implement Phase 3 local CRUD service and LINQ filters`: verified in local Git history at the start of Phase 4.
- `f191e39` — `Implement Phase 4 SQLite persistence`: verified in local Git history at the start of Phase 5.
- `7639dff` — `Implement Phase 5 offline MAUI task interface`: verified in local Git history at the start of Phase 6.
- `9935192` — `Implement Phase 6 task REST API`: verified in local Git history at the start of Phase 7.
- 5810242 — Implement Phase 7 PostgreSQL and EF Core persistence: verified in local Git history at the start of Phase 8.
- Phase 8 has no committed milestone yet. No merge or force-push has been performed.

## Next task

Configure the database/JWT secret and apply pending migrations using DATABASE.md, then review Phase 8. Next is Phase 9 client API integration: login/logout, configurable endpoint, bearer attachment, secure-storage/expiry failures, and account-aware local boundaries. Keep offline CRUD usable and do not implement synchronization automatically.

## What to learn now

Trace registration through framework hashing, JWT issue/validation, and claim-derived task ownership. Learn authentication versus authorization, why decoded claims alone are untrusted, why owner IDs never come from JSON, and how migration policy protects anonymous data. Understand short token lifetime/local logout and why platform SecureStorage has no plaintext fallback. See LEARNINGS for examples.
