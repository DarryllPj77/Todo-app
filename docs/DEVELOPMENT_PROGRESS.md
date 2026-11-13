# Development progress

Last updated: **2026-11-13 (Asia/Manila)**.

## Current phase

**Phase 10 - Offline account synchronization implemented; native acceptance pending.** Synced tasks provides a separate account/origin SQLite cache, transactional outgoing operations, idempotent uploads, revision cursors, retained tombstones, bounded retries, and explicit conflicts. The final full suite passed **190 tests, 0 failed, 0 skipped**, including 27 PostgreSQL facts. The final Windows-targeted solution build passed with **0 warnings, 0 errors**. Apply AddTaskSynchronization to your own development database before restarting the API. Original device-only tasks remain separate. Native HTTPS/storage/connectivity/timer/restart and two-device UI acceptance remain pending; see SYNCHRONIZATION.md.

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
- Added the Phase 8 MAUI SecureTokenStore foundation using SecureStorage, with no password/plaintext fallback; Phase 9 now wires login/session behavior.
- Added nine real PostgreSQL authentication/isolation facts and five portable settings cases. Existing tests use explicit fake auth only in contract/persistence fixtures; actual JWT tests use real middleware.
- Updated .http samples to placeholder credentials/private bearer-token variables and added signing-key generation/migration instructions.

- Added client API DTOs, long-lived HttpClient, per-request bearer attachment, HTTPS-only origin validation, disabled automatic redirects, and friendly status/network/timeout/cancellation handling.
- Added ClientSession with protected session-envelope persistence, online restoration verification, expiry, serialized storage operations, session-version guards, and a persisted restoration block for failed secure removal.
- Added AccountPage/AccountViewModel and a separate ServerWorkspaceViewModel with registration/login/logout, address configuration, server CRUD/filter/completion, native delete confirmation, and uncertain-write refresh gating.
- Added Account & server navigation from the local toolbar. Local SQLite is neither reassigned nor uploaded; server rows/drafts clear on session/address changes.
- Added 36 client cases, including a real API/JWT/PostgreSQL round trip. Tests cover overlapping logins, restore/logout races, old responses, storage failures, endpoint validation, credentials, errors, and uncertain saves.
- Rebuilt the native Windows solution and launched the client. The task-list Account toolbar was observed. UI interaction stopped when user input was detected; Account layout/native auth/storage are not claimed.
- Updated README and all five documentation files. No new packages, migrations, Domain/API behavior, or SQLite schema changes were required.

- Added shared SyncTaskData/Mutation/Entry/Result/Page records and a validated TaskItem.CreateWithId factory for sync identity preservation.
- Added SyncRecords/SyncReceipts EF mappings and generated AddTaskSynchronization migration/designer/snapshot. Normal REST writers now share the account transaction lock/journal with sync uploads.
- Added authenticated SyncController/SyncService with operation receipts, expected revisions, owner isolation, validation, cursor pages, tombstones, and atomic write/journal/receipt commits. Existing owned tasks bootstrap lazily; anonymous rows stay unclaimed.
- Added SqliteSyncStore in a separate sync.db3 version-1 schema: origin/account partitions, cached intent, immutable outbox operations, retry metadata, conflicts, and transactional page/cursor application. Existing todoapp.db3 is unchanged.
- Added SyncEngine, SyncWorkspaceViewModel, SyncPage, toolbar navigation, local queue/edit/delete, visible status, Keep mine / Use server, and visible-page connectivity/timer retry scheduling. No closed-app worker or device-task import exists.
- Added 27 regression cases: 14 real-file store cases, five engine/ViewModel cases, eight PostgreSQL sync facts. Tests include concurrent replay, lost replies, stale receipts, local edits during pending uploads, deletion, rollback, cursor pages, legacy bootstrap, account switching, stale drafts, stale conflict choices across windows, and two-replica convergence.
- Corrected pooled SQLite handles retaining temporary files. Sync store connections disable pooling so disposal releases the file. An older startup-failure test hit sandbox EventLog permissions; normal-access verification passed without weakening the application/test assertion.
- The first isolated Windows restore was blocked from NuGet. Normal-access restore succeeded; forcing metadata refresh removed cached NU1900 warnings. Final solution/XAML build passed with zero warnings/errors in ignored .tools/phase10-native output, preserving the older running app.
- Added a final revision check on conflict choices so another window cannot silently resolve against an unseen server state; 19 targeted sync checks and the updated Windows solution build passed.
- Final verification passed 190 tests with no skips after the conflict-choice guard. Two generated databases from the earlier interrupted sandbox run were removed by exact name; the final run left no test databases. The isolated cluster was stopped and the installed PostgreSQL service remains running.
- Added SYNCHRONIZATION.md with protocol, design decisions, limits, migration upgrade, and manual acceptance steps; updated the existing six documentation files.

## Build and test evidence

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 0 warnings, 0 errors |
| Full CLI suite using TODOAPP_TEST_POSTGRES and the script's isolated artifacts path | Passed: 190 tests, 0 failed, 0 skipped (final normal-access CLI run with the same isolated test connection/artifacts path) |
| Windows-targeted `TodoApp.slnx` build using local SDK and TodoAppTargetFrameworks | Passed: all five projects, 0 warnings, 0 errors |
| Final Phase 10 native build output | Passed: isolated .tools/phase10-native, all five projects, 0 warnings/errors; old app was not stopped |
| Final test result | .tools/phase10-results/phase10-final.trx: 190 passed, 0 failed, 0 skipped |
| Default portable test mode without TODOAPP_TEST_POSTGRES | Twenty-seven PostgreSQL facts are explicitly skipped (163 portable cases); portable REST tests use explicit memory/auth substitutes |
| Live HTTP CRUD on temporary loopback port 5073 | Passed: POST 201, GET/PUT 200, DELETE 204, final GET []; listener stopped |
| `scripts/Run-Windows.ps1 -BuildOnly` (Phase 5 check) | Passed: native Windows MAUI/XAML build, 0 warnings, 0 errors; MAUI source unchanged in Phase 6 |
| Initial global workload installation | Canceled during elevation and rolled back; local portable workload installation subsequently succeeded |
| Initial generic TargetFrameworks override after local setup | NETSDK1005 in shared libraries; fixed using the client-only TodoAppTargetFrameworks property |
| Full solution build (historical Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload |
| Windows native direct launch | Passed: task-list empty state and editor visible in dark mode; app left available for user testing |
| Full native CRUD/restart, light/narrow/mobile acceptance | Pending README manual checklist; not claimed from ViewModel/storage tests |
| SQLite native execution on Windows | Passed: temporary file reopen/round-trip/rollback tests; files cleaned up after each test |
| PostgreSQL 18 verification | Passed: 27 real-database facts, including sync/receipt/concurrency/two-replica checks and existing client/auth/isolation/migration coverage; each fixture cleans up its own database |

Initial sandbox build attempts in Phase 1 returned no diagnostics; normal SDK access produced the core results. Phase 5 compiled native Windows/XAML with a local workload. Phase 6 added 32 tests for the actual API pipeline: routes, response contracts, missing IDs, malformed JSON/values, length limits, unknown/server-owned fields, all filters, category reuse, parallel creates, cancellation, fresh server state, and refusal to start in Production. Integration tests use an in-memory HTTPS request host, not a deployed TLS certificate. A separate loopback HTTP listener passed real socket CRUD checks. The HTTP-only profile logged its expected missing HTTPS redirect-port warning. An initial smoke assertion mishandled PowerShell's empty-array wrapping; checking the final JSON body directly verified it was []. No application change was needed for that assertion correction.

Presentation tests retain their Phase 5 coverage of validation, draft isolation, cancel, retry, confirmations, missing tasks, and double-save/navigation failures. They do not simulate native clicks or theme switching.

In Phase 7, the running Phase 6 API initially locked normal build outputs. Tests and migration generation proceeded in isolated .tools/phase7-build output without stopping it. The user then reported stopping the API; the final Windows-targeted solution build used normal output successfully. Test factory configuration was moved to validation after host configuration was built, allowing an explicit test substitute while normal startup still rejects a missing connection. Final database tests used scripts/Test-Postgres.ps1 and did not touch the installed service's databases.

Phase 8 tested hashed registration/login, unique email, password whitespace preservation, generic credential failure, auth rate limiting, missing/invalid/expired/unsigned/wrong-key/issuer/audience tokens, unknown/deleted accounts, every task route's authentication/ownership behavior, per-user categories, spoofed owner input, and migration preservation of legacy rows. No real signing secret/password/token was committed or printed. Phase 8 did not add login UI; Phase 9 now implements it. Native secure token round trips, refresh tokens, password reset/email verification, and production identity/deployment acceptance are not claimed.

Phase 9 adds 36 cases to the Phase 8 total of 127: 26 session/client cases, nine account/workspace cases, and one real PostgreSQL client/API fact. The full suite passed 163/163 with no skips. The client integration uses real JWT middleware and PostgreSQL with WebApplicationFactory's in-memory HTTPS transport and a fake platform store; it does not prove native socket TLS or OS SecureStorage. No certificate bypass was added. The isolated test cluster was stopped after verifying no generated test databases remained; the existing PostgreSQL 18 service remains running. The last production-code Windows build passed with zero warnings/errors; the final additional overlapping-login test then passed in the full suite.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions. MVC.Testing, JwtBearer, EF Core/Relational/Design/tool are aligned at 10.0.15; Npgsql EF 10.0.3 supports the EF 10 version. Microsoft.Data.Sqlite remains 10.0.15. Password hashing/rate limiting come from ASP.NET Core, and SecureStorage from MAUI.

## Work in progress / pending verification

- Complete the README native checklist: create/edit/cancel/toggle/filter/delete a disposable test task, close/reopen the app, and verify persistence.
- Check system light mode, narrow windows, accessibility, and mobile device targets. Only Windows has been compiled/launched in this run.
- Empty categories have no management/browser UI yet; typing a category name reuses one visible among stored tasks or creates a new category, rather than renaming it globally.
- Follow DATABASE.md to set your development connection and a cryptographically random Base64 Jwt:SigningKey, then apply all three migrations. No user credentials or persistent app connection were invented during this run.
- Legacy anonymous tasks/categories remain unowned and hidden. Any reassignment needs explicit administrative review; registration does not claim old data.
- Review Phase 10 and complete the SYNCHRONIZATION.md native acceptance checklist before notifications. No refresh token/revocation/password-reset/email-verification system is implemented; the API remains Development-only.
- Session and synced UI/storage code compile on Windows; actual device HTTPS, acquisition/restoration/expiry/logout, native layout/connectivity/timer, and two-device acceptance remain pending. Device-only SQLite schema is unchanged; sync.db3 is a separate version-1 cache.
- API DueToday currently follows the server's local date; define a per-user time-zone policy before release; sync dates are calendar values. Production deployment remains later work.

## Uncommitted changes

Phase 10 changes are uncommitted; no commit or push was performed for this phase. Phase 9 was committed/pushed as `079753a` in the preceding turn. The user-added untracked `.vscode/` directory remains outside scope. Portable tools, logs, test-cluster files, and build output stay ignored.

- New: Domain/Sync contracts; SyncController/SyncJournal/SyncService; server SyncRecords and AddTaskSynchronization migration/designer; SqliteSyncStore; SyncEngine/SyncWorkspaceViewModel/SyncPage; SyncStoreTests/SyncEngineTests/PostgresSyncTests; docs/SYNCHRONIZATION.md.
- Modified: server DI/task transactions/model snapshot, TaskItem identity factory, MAUI client/DI/navigation/toolbar, test links/fakes/migration count, README and all five existing docs.
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

Phase 9 preserves the server contract and device-local CRUD while adding an independent authenticated online workspace. Shared Domain/SQLite source/schema remain unchanged. .tools, bin, obj, and SQLite files/journals remain excluded from Git scope.

## Committed milestones

- `2b24e81` — `Create Todo App Phase 1 foundation`: all 69 foundation files committed after reviewing staging scope and rerunning the core integration test (1 passed).
- `00ee06a` — `docs: record initial foundation milestone`: foundation publication verified against GitHub on main in the preceding run.
- `0e50592` — `Implement Phase 2 domain models and business rules`: verified in local Git history at the start of Phase 3.
- `337e418` — `Implement Phase 3 local CRUD service and LINQ filters`: verified in local Git history at the start of Phase 4.
- `f191e39` — `Implement Phase 4 SQLite persistence`: verified in local Git history at the start of Phase 5.
- `7639dff` — `Implement Phase 5 offline MAUI task interface`: verified in local Git history at the start of Phase 6.
- `9935192` — `Implement Phase 6 task REST API`: verified in local Git history at the start of Phase 7.
- 5810242 — Implement Phase 7 PostgreSQL and EF Core persistence: verified in local Git history at the start of Phase 8.
- `c0fae61` ? `Implement Phase 8 authentication and user ownership`: verified in local Git history during Phase 9.
- `079753a` - `Implement Phase 9 MAUI account and API integration`: committed and pushed to origin/main in the preceding user-authorized turn.
- Phase 10 has no committed milestone yet. No merge or force-push has been performed.

## Next task

Apply AddTaskSynchronization to your configured development database, rebuild the client after closing the older app, and follow SYNCHRONIZATION.md's offline/restart/two-device/conflict checklist. Review Phase 10 before Phase 11 notifications. No notification scheduler starts automatically. Native/mobile acceptance, cache encryption/access policy, retention, server-restore/reset reconciliation, and production deployment remain open release gates.

## What to learn now

Trace Save locally & queue through one SQLite transaction, then follow its immutable operation ID to a server receipt. Learn why a timeout cannot prove failure, why stale receipts cannot override newer downloads, and why revisions/tombstones/cursors are different concepts. Compare explicit Keep mine / Use server with silently choosing by device timestamps. See LEARNINGS and SYNCHRONIZATION.md for examples.
