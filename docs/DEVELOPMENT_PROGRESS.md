# Development progress

Last updated: **2026-11-14 (Asia/Manila)**.

## Current phase

**Phase 14A manual foreground location reminders implemented; native/device acceptance pending.** Added protected task-linked places, explicit one-shot location requests, conservative accuracy/freshness rules, outside-before-inside state, durable one-attempt consumption and explicit rearm/disable/delete. No background geofence/listener or location upload runs. All **569 tests passed, 0 failed, 0 skipped** (510 portable, **59 PostgreSQL facts**); final Windows solution/XAML build has **0 warnings/errors**. Actual GPS/permissions/notifications, mobile and earlier Google/provider acceptance remain open. Next increment: Phase 14B native background geofence lifecycle/platform design; see LOCATION_REMINDERS.md.

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

- Added Domain/Reminders models and deterministic date/time-to-UTC rules: explicit opt-in, original zone/wall-time retention, fixed instant on travel, 1-minute/365-day bounds, and rejection of skipped/repeated DST times.
- Added independent SqliteReminderStore (reminders.db3, schema version 1) with transactional stable IDs, durable desired/canceled/attempted state, diagnostic status, and rejection of corrupt/newer data without reset.
- Added ReminderTaskCatalog/ReminderService/IReminderPlatform with active-scope isolation, nearest-32 future policy, permission checks without automatic prompts, replacement/cancellation, task completion/deletion/due-date review, and generic payloads.
- Added ReminderAwareTaskService after successful device writes and an awaited, exception-isolated SqliteSyncStore.Written callback after committed cached writes/downloads. Notification/UI errors cannot turn a committed task create into a failed save.
- Added ReminderRuntime startup/resume/account reconciliation and a 15-second running-app timer. Windows saves a one-attempt marker before native show, allows five-minute catch-up, and avoids repeated attempts after uncertainty/clock rollback. No closed-app Windows service was added.
- Added Windows AppNotificationManager adapter; Android permission/channel/inexact-alarm/receiver/boot-restoration source and icon/manifest permissions; Apple local-request/foreground-delegate source. Only Windows is compiled here. No exact-alarm permission, notification plugin, remote push, or recurrence was introduced.
- Added RemindersViewModel, RemindersPage, scoped registration, toolbar navigation, explicit permission/save/cancel/retry/status UI. Choices are per-device and do not change server/sync task payloads.
- Added 26 portable policy/storage/ViewModel cases covering UTC/DST, permission denial, replacement, title changes, task/account cancellation, one-cancel failure isolation, scheduling/sign-out races, stable IDs, caps, missed/uncertain attempts, corrupt schema/state, and failure-safe task/sync hooks.
- Full final suite passed 216 tests with no skips; Windows solution/XAML build passed with zero warnings/errors in .tools/phase11-native, without stopping/overwriting the older running app. Fake notification adapters do not prove actual native delivery, permissions, or mobile compilation.
- Isolated PostgreSQL cleanup verified zero generated databases, then stopped the test cluster; the installed PostgreSQL 18 service remains running.
- Added NOTIFICATIONS.md and updated README/architecture/database/learnings/roadmap/progress plus the sync integration note. No user credentials or development databases were changed.

- Added Domain/Views immutable query/candidate/saved-definition models, validation/normalization, literal case-insensitive search, AND-combined predicates, device-calendar relative dates, and deterministic title/due/priority sorts.
- Added SqliteTaskViewStore using a separate views.db3 version-1 schema: parameterized SQL, per-scope unique names, a 50-definition quota, transactional create/update/rename/delete, and revisions that reject stale concurrent writes/deletes. Corrupt/newer metadata is rejected without reset.
- Added TaskViewRow/TaskViewsViewModel and TaskViewsPage for filter drafts versus applied rules, saved definitions, matching task details, reset/refresh, explicit source navigation, and confirmed definition-only deletion.
- Added main-toolbar Saved views navigation and scoped registrations. Source generations/session versions reject stale reads; account/API/sign-out clears account definitions/drafts/results while device drafts remain independent of login.
- Account views read only the selected scope's cache and expose whole-cache queue/conflict totals even for empty filters. Applying/deleting a saved definition writes no task, outbox, conflict, or reminder state; device editing uses the existing reminder-aware editor.
- Added 35 cases: 15 pure query cases, nine SQLite store cases, and 11 actual ViewModel cases. Targeted TaskView checks (including 16 existing presentation cases) passed 51/51.
- Final full suite passed 251/251 with no skips, including all existing reminder/sync/JWT/PostgreSQL checks. Windows solution/XAML build passed with 0 warnings/errors in .tools/phase12a-native, preserving the older running app. Actual native screen/mobile acceptance is pending.
- Verified zero generated test databases after the final run, stopped the isolated cluster, and confirmed the installed PostgreSQL 18 service remains running.
- Added SAVED_VIEWS.md and updated README/architecture/database/learnings/roadmap/progress. Split Phase 12 into explicit A-G increments; tags, relationships, urgency/importance, recurrence, focus history, and the full adaptive workspace remain pending.

- Added immutable TagSet with bounded canonical labels, NFC/lowercase normalization, duplicate removal, ordinal ordering, value equality/hash, CSV parsing, and an explicit JSON-array converter. Record equality now compares tag contents rather than mutable list references.
- Extended TaskItem/TaskDetails and local/API models with tag sets. Omitted/null tags preserve existing values on update; explicit empty sets clear them. New task editors always submit an explicit set, while completion toggles preserve it.
- Added atomic SQLite device schema 1-to-2 Tags column/default upgrade, tag hydration/writes, and no-reset future-version checks. Added EF immutable JSON conversion/comparer and generated 20261113092317_AddTaskTags migration/designer/snapshot; existing rows get []. No package was added.
- Extended authenticated API DTOs, PostgreSQL/in-memory services, journal snapshots, sync apply, and response models. Validation rejects malformed/over-limit tag arrays before mutation; task ownership remains claim-derived.
- Added SyncPage ProtocolVersion=2 and a capability gate before tag-aware uploads. Unsupported APIs block the same retained operation for explicit Retry now after upgrade. Null Tags remain omitted from old serialized operations so stored receipt request bytes still match.
- Upgraded sync.db3/views.db3 format versions to 2 without rewriting old pending or definition JSON. Deep tag equality keeps acknowledgements finite; later explicit clears remain separate from immutable in-flight creates.
- Added tag inputs/summaries/conflict-set display in device/server/sync editors and saved-view Any/AllListed/AnyListed/Untagged rules plus literal tag search. Existing categories, whole-task conflict policy, and reminder cancellation semantics remain independent.
- Added 25 cases: 20 portable tag/domain/SQLite/query/editor/queue/compatibility checks and five real PostgreSQL REST/migration/receipt/two-replica cases. Targeted checks passed 20/20, including final frozen-payload/clear chaining assertions.
- Complete suite passed 276/276 with no skips; final Windows solution/XAML build passed with 0 warnings/errors in .tools/phase12b-native. A sandbox-only startup test encountered the known Windows EventLog permission error; normal-access full verification passed without weakening the assertion.
- Verified zero generated test databases, stopped the isolated cluster, and confirmed the installed PostgreSQL 18 service remains running.
- Added TAGS.md, updated HTTP samples/README and all relevant docs with migration/contract/local-format upgrade rules. Existing user databases/app processes were not modified; native UI/mobile/TLS acceptance remains separate from test-host behavior.

## Build and test evidence

Phase 12F adds 32 portable cases for duration bounds, completion/paused time, break exclusion, monotonic/wall-clock changes, heartbeat gaps, checkpoint/restart/suspension recovery, task/occurrence snapshots, account/origin/expiry/lookup races, device independence, exclusive app-instance leases, concurrent starts, stale/history rewrite protection, forced storage failures, corrupt/newer files, fractional aggregation, UI drafts/totals, manual long-break suggestions, and stale-window controls. Existing 59 PostgreSQL facts are unchanged and remain in the full regression gate. Native interaction/lifecycle/device acceptance is separate from these tests and Windows compilation.

| Check | Result |
| --- | --- |
| `dotnet build TodoApp.Core.slnf --no-restore --nologo -m:1` | Passed: 0 warnings, 0 errors |
| Full CLI suite using TODOAPP_TEST_POSTGRES and isolated artifacts | Passed: 404 tests, 0 failed, 0 skipped (normal-access CLI with the isolated test connection) |
| Windows-targeted `TodoApp.slnx` build using local SDK and TodoAppTargetFrameworks | Passed: all five projects, 0 warnings, 0 errors |
| Final Phase 12F native build output | Passed: isolated .tools/phase12f-native, all five projects, 0 warnings/errors; no app stop required |
| Final test result | .tools/phase12f-results/phase12f.trx: 404 passed, 0 failed, 0 skipped |
| Final Phase 12F focus verification | .tools/phase12f-results/phase12f-focus-final.trx: 32 passed, 0 failed, 0 skipped after final scope guard refinement |
| Default portable test mode without TODOAPP_TEST_POSTGRES | Fifty-nine PostgreSQL facts are explicitly skipped (345 portable cases); portable REST tests use explicit memory/auth substitutes |
| Live HTTP CRUD on temporary loopback port 5073 | Passed: POST 201, GET/PUT 200, DELETE 204, final GET []; listener stopped |
| `scripts/Run-Windows.ps1 -BuildOnly` (Phase 5 check) | Passed: native Windows MAUI/XAML build, 0 warnings, 0 errors; MAUI source unchanged in Phase 6 |
| Initial global workload installation | Canceled during elevation and rolled back; local portable workload installation subsequently succeeded |
| Initial generic TargetFrameworks override after local setup | NETSDK1005 in shared libraries; fixed using the client-only TodoAppTargetFrameworks property |
| Full solution build (historical Phase 1 check) | Blocked: NETSDK1147, missing maui-android workload |
| Windows native direct launch | Passed: task-list empty state and editor visible in dark mode; app left available for user testing |
| Full native CRUD/restart, light/narrow/mobile acceptance | Pending README manual checklist; not claimed from ViewModel/storage tests |
| SQLite native execution on Windows | Passed: temporary file reopen/round-trip/rollback tests; files cleaned up after each test |
| PostgreSQL 18 verification | Passed: 59 real-database facts including recurrence migrations/counters/tombstones, ownership, concurrent completion, receipt replay, and replicas; zero generated databases remain, isolated cluster stopped, installed service remains Running |

Initial sandbox build attempts in Phase 1 returned no diagnostics; normal SDK access produced the core results. Phase 5 compiled native Windows/XAML with a local workload. Phase 6 added 32 tests for the actual API pipeline: routes, response contracts, missing IDs, malformed JSON/values, length limits, unknown/server-owned fields, all filters, category reuse, parallel creates, cancellation, fresh server state, and refusal to start in Production. Integration tests use an in-memory HTTPS request host, not a deployed TLS certificate. A separate loopback HTTP listener passed real socket CRUD checks. The HTTP-only profile logged its expected missing HTTPS redirect-port warning. An initial smoke assertion mishandled PowerShell's empty-array wrapping; checking the final JSON body directly verified it was []. No application change was needed for that assertion correction.

Presentation tests retain their Phase 5 coverage of validation, draft isolation, cancel, retry, confirmations, missing tasks, and double-save/navigation failures. They do not simulate native clicks or theme switching.

In Phase 7, the running Phase 6 API initially locked normal build outputs. Tests and migration generation proceeded in isolated .tools/phase7-build output without stopping it. The user then reported stopping the API; the final Windows-targeted solution build used normal output successfully. Test factory configuration was moved to validation after host configuration was built, allowing an explicit test substitute while normal startup still rejects a missing connection. Final database tests used scripts/Test-Postgres.ps1 and did not touch the installed service's databases.

Phase 8 tested hashed registration/login, unique email, password whitespace preservation, generic credential failure, auth rate limiting, missing/invalid/expired/unsigned/wrong-key/issuer/audience tokens, unknown/deleted accounts, every task route's authentication/ownership behavior, per-user categories, spoofed owner input, and migration preservation of legacy rows. No real signing secret/password/token was committed or printed. Phase 8 did not add login UI; Phase 9 now implements it. Native secure token round trips, refresh tokens, password reset/email verification, and production identity/deployment acceptance are not claimed.

Phase 9 adds 36 cases to the Phase 8 total of 127: 26 session/client cases, nine account/workspace cases, and one real PostgreSQL client/API fact. The full suite passed 163/163 with no skips. The client integration uses real JWT middleware and PostgreSQL with WebApplicationFactory's in-memory HTTPS transport and a fake platform store; it does not prove native socket TLS or OS SecureStorage. No certificate bypass was added. The isolated test cluster was stopped after verifying no generated test databases remained; the existing PostgreSQL 18 service remains running. The last production-code Windows build passed with zero warnings/errors; the final additional overlapping-login test then passed in the full suite.

Package choices: MAUI Controls 10.0.130 matches the stable official .NET 10 template checked against NuGet. Logging.Debug 10.0.0 is template-supplied. Test SDK 17.14.1, xUnit 2.9.3, and runner 3.1.4 are SDK-template versions. MVC.Testing, JwtBearer, EF Core/Relational/Design/tool are aligned at 10.0.15; Npgsql EF 10.0.3 supports the EF 10 version. Microsoft.Data.Sqlite remains 10.0.15. Password hashing/rate limiting come from ASP.NET Core, and SecureStorage from MAUI.

## Work in progress / pending verification

- Complete the README native checklist: create/edit/cancel/toggle/filter/delete a disposable test task, close/reopen the app, and verify persistence.
- Check system light mode, narrow windows, accessibility, and mobile device targets. Only Windows was compiled in Phase 12F; focus timer/lifecycle and recurrence/planning/dependency/hierarchy native interaction and prior reminder delivery remain unverified. No running user app was stopped.
- Empty categories have no management/browser UI yet; typing a category name reuses one visible among stored tasks or creates a new category, rather than renaming it globally.
- Follow DATABASE.md to set your development connection and a cryptographically random Base64 Jwt:SigningKey, then apply all eight migrations. No user development database migration or persistent connection change ran here.
- Legacy anonymous tasks/categories remain unowned and hidden. Any reassignment needs explicit administrative review; registration does not claim old data.
- Review Phase 12F using FOCUS.md and retain RECURRENCE.md/EISENHOWER.md/DEPENDENCIES.md/HIERARCHY.md/TAGS.md/SAVED_VIEWS.md/NOTIFICATIONS.md/SYNCHRONIZATION.md acceptance gates. No refresh token/revocation/password-reset/email-verification system is implemented; the API remains Development-only.
- Verify Reminders native permission/display/cancellation/time/account behavior using NOTIFICATIONS.md. Android/Apple adapters need compilation/device checks, and Windows closed-app delivery remains outside this unpackaged implementation.
- Session and synced UI/storage code compile on Windows; actual device HTTPS, acquisition/restoration/expiry/logout, native layout/connectivity/timer, and two-device acceptance remain pending. Device tasks/sync use schema 6, saved views 3, reminders 1, and new focus history 1.
- API DueToday currently follows the server's local date; define a per-user time-zone policy before release; sync dates are calendar values. Production deployment remains later work.

## Uncommitted changes

Phase 12F changes are uncommitted; no commit or push ran. Phase 12E is recorded in local commit `1482320`, verified at the start. User-added untracked `.vscode/` remains excluded. Build/test/tool/cluster files stay ignored; the empty focus lease companion is now ignored too.

- New: Domain focus/session rules, SqliteFocusStore, FocusTaskCatalog/FocusService/FocusRuntime, Focus ViewModel/page, FocusTests, docs/FOCUS.md.
- Modified: native window lifecycle/DI/navigation/task-list toolbar, portable test-source links and navigation doubles, .gitignore, README and docs. Existing task/API/sync/model/migration source remains unchanged.
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
- `2a7fe54` - `Implement Phase 10 offline account synchronization`: verified in local Git history at the start of Phase 11.
- `ab07130` - `Implement Phase 11 device-local task reminders`: verified in local Git history at the start of Phase 12A.
- `b2d860e` - `Implement Phase 12A custom filters and saved views`: verified in local Git history at the start of Phase 12B.
- `0bacb27` - `Implement Phase 12B task tags and saved-view tag filters`: verified at the start of Phase 12C.1.
- `f0dc3e1` - `Implement Phase 12C.1 task hierarchy and subtasks`: verified at the start of Phase 12C.2.
- `8329aef` - `Implement Phase 12C.2 task dependencies`: verified at the start of Phase 12D.
- `6464399` - `Implement Phase 12D urgency and importance planning`: verified at the start of Phase 12E.
- `1482320` - `Implement Phase 12E completion-based task recurrence`: verified at the start of Phase 12F.
- `66dd406` - `Implement Phase 12F focus sessions and local time history`: verified at the start of Phase 13A.
- `74d3aef` - `Implement Phase 12G adaptive MAUI workspace`: verified at the start of Phase 13A. No merge or force-push ran.

## Next task

Follow GOOGLE_MONITORING.md with a configured Google test calendar for real full/incremental/reset, privacy, backoff and page/window lifecycle acceptance; complete earlier Google/SecureStorage/mobile/release gates. Next planned feature is Phase 14 location reminders, beginning with supported-platform/privacy design. Provider writes and local adoption remain explicit reviewed choices; no unattended mutation is enabled.

## What to learn now

Trace complete pages into one cursor/notices transaction, and compare the same active link cohort before acknowledging it. Distinguish remote scan reset from local pending intent, read-only opt-in detection from automatic conflict resolution, and durable retry delay from a background job. See LEARNINGS.md and GOOGLE_MONITORING.md.

## Phase 12G verification and delivery

- Six added workspace regression cases; 22 targeted list/editor/workspace tests pass. Full suite: 410 passed, 0 skipped; isolated PostgreSQL test databases remaining: 0. Repository test cluster stopped; installed PostgreSQL service remains running.
- Windows solution/XAML build passed with 0 warnings/errors after normal-access NuGet restore; initial sandbox restore failed without changing dependencies.
- Isolated Debug preview showed the wide three-pane workspace, category filtering and selected details. User stopped Computer Use before compact inspection. Preview runtime is separate from normal app data; mobile and full native acceptance remain pending.
- Phase 12G was uncommitted at delivery over 66dd406 and is now committed as 74d3aef (verified this turn). User .vscode/ files remain excluded from this task. No commit/push was executed by the assistant.

## Phase 13A delivery

- Domain/Calendar defines TimeBlock/TimeBlockDraft, start/duration/DST rules, half-open overlap, and deterministic escaped/folded UTF-8 RFC 5545 output with stable UID/SEQUENCE.
- SqliteCalendarStore adds independent schema-1 calendar.db3, per-scope reads, transactional overlap/500-block/revision checks, immutable task/title identity, and unsupported/corrupt-data failure without reset. Existing task/sync/views/reminders/focus/server formats are unchanged.
- CalendarViewModel/page supports device/current-account cached tasks, local block CRUD, draft discard confirmation, stale export checks and account/version/expiry guards. File export shares only a selected saved block and has a dedicated Android cache FileProvider root plus iPad anchor. No provider/token/network integration was added.
- Added 21 calendar cases. Full regression: 431 passed, 0 skipped (372 portable and 59 PostgreSQL). Final calendar cases were rechecked after tightening persisted-data validation. Windows build/native acceptance details are recorded in CALENDAR.md.
- Phase 13A was uncommitted at delivery and is now committed as 8e15128 (verified at the start of Phase 13B). Phase 12G is committed; unrelated .vscode/ remains excluded. The assistant did not commit, pull, push, create a Google client or send anything to Google.

## Phase 13B verification and delivery

- Added 26 OAuth/lifecycle/real-loopback cases. Full regression: 457 passed, 0 skipped; Windows solution/XAML build: 0 warnings/errors. Google endpoints use mock responses; no live Google account was contacted. Native consent/SecureStorage/mobile checks remain open.
- Isolated PostgreSQL test databases remaining: 0; repository test cluster stopped, installed PostgreSQL service remains running. No app windows or user development servers were stopped.
- Phase 13A milestone 8e15128 is committed and verified. Phase 13B was uncommitted at delivery and is now committed as bc62835 (verified at the start of Phase 13C); .vscode/ is excluded. No commit/pull/push, Google project/client creation or provider authorization was performed by the assistant.

## Phase 13C verification and delivery

- Added 23 event-link cases. Seventy targeted calendar/OAuth/link checks passed; full regression: 480 passed, 0 skipped (421 portable and 59 PostgreSQL). Final Windows solution/XAML build: 0 warnings/errors. Tests cover schema-1 upgrade preservation, journal CAS/concurrency, reserved-ID/409 recovery, lost create/update/delete replies, captured-snapshot retry, ownership/type/412 conflicts, orphan cleanup, identity/destination pinning, pagination and account-confirmation/write races.
- calendar.db3 upgrades to schema 2; existing block data is preserved. No new server migration, task/sync schema or protocol change. Provider tokens remain in SecureStorage; the SQLite journal contains linkage metadata only.
- Provider HTTP is mocked; no actual Google account/events, native confirmations or mobile flow were operated. Live/provider/native acceptance remains open. Isolated PostgreSQL test databases remaining: 0; repository cluster stopped and installed PostgreSQL service remains running.
- Phase 13B milestone bc62835 is committed and verified. Phase 13C was uncommitted at delivery and is now committed as 0dfcc8b (verified at the start of Phase 13D.1); unrelated .vscode/ remains excluded. No commit, pull, push or provider write was performed by the assistant.

## Phase 13D.1 verification and delivery

- Added 25 review cases; all 95 targeted calendar/OAuth/reconciliation checks pass. Full regression: 505 passed, 0 skipped (446 portable, 59 PostgreSQL). Final Windows solution/XAML build: 0 warnings/errors. Google HTTP is mocked; no provider/native automation was performed.
- calendar.db3 upgrades from 2 to 3 without resetting blocks/links. Tests cover persisted review/read-only snapshots, atomic adoption/overlap rollback, stale local/provider/replaced-review choices, reviewed conditional push/412/lost reply, missing/reappearing events, unsafe detach, orphan/no resurrection, exact UTC DST and local-year boundary handling, confirmation/logout and pending recovery gates.
- Local adoption changes only the calendar block title/times and acknowledged link baseline. Source task identity/fields, reminders, focus history and task sync remain unchanged. No background sync or incremental cursor is enabled; that remains 13D.2.
- Isolated PostgreSQL test databases remaining: 0; repository cluster stopped and installed PostgreSQL service remains running. Phase 13C milestone 0dfcc8b is committed and verified; Phase 13D.1 was uncommitted at delivery and is now committed as d54cee4 (verified at the start of Phase 13D.2). .vscode/ is excluded; no commit/pull/push/provider write was performed by the assistant.

## Phase 13D.2 verification and delivery

- Added 24 feed/monitor cases; 119 targeted calendar/OAuth/reconciliation/feed checks passed. Full regression: 529 passed, 0 skipped (470 portable and 59 PostgreSQL). Final Windows solution/XAML build: 0 warnings/errors. Google HTTP is mocked; actual Google and native monitor lifecycle acceptance remain open.
- calendar.db3 upgrades transactionally to schema 4 with monitor profiles and linked-event notices. Tests cover schema preservation, complete pages, stable/escaped cursor queries, partial failure, invalid paging, 410 mid-page/full reset/failure, unlinked data discard, notices/stale reviews, preserved pending intent, cohort/profile races, opt-in/backoff/Retry-After/manual override, auth pause, local disable/opt-out precedence, cancellation/account switching, corrupt metadata and bounds.
- Opt-in checks operate only while the monitor page/window is active and online. No event publish, adoption, deletion, pending-write retry, task completion or OS background job is automatic. Existing file/protocol/server formats otherwise remain unchanged.
- PostgreSQL test databases remaining: 0; repository cluster stopped and installed service remains running. Phase 13D.1 milestone d54cee4 is committed/verified. Phase 13D.2 was uncommitted at delivery and is now committed as 206cd84 (verified at the start of Phase 14A); .vscode/ is excluded. No commit/pull/push/live provider write was performed by the assistant.

## Phase 14A verification and delivery

- Added 40 location cases covering Haversine/date-line bounds, invalid drafts, stale/future/mock/inaccurate/boundary fixes, initial-inside suppression, outside-to-inside state, one attempt/restart/rearm, denied/unavailable notifications, native failure consumption, task/occurrence changes, sync conflicts, cancellation/account/queued-action races, protected storage/tampering/missing keys, scope/revision isolation, concurrent checks and retained disabled metadata after failed cancellation.
- Full regression: 569 passed, 0 skipped (510 portable and 59 PostgreSQL). Final isolated Windows solution/XAML build: 0 warnings/errors. Android manifest and Apple plist/entitlement XML validated; mobile targets/devices were not built or operated.
- New location.db3 schema 1 uses AES-GCM with an OS SecureStorage-only key and nonreused negative notification IDs. Existing local schemas, eight PostgreSQL migrations and sync protocol 6 are unchanged. No real location, permission prompt or native notification was exercised; no coordinates/provider events were uploaded.
- Added LOCATION_REMINDERS.md with usage, arrival/privacy/storage semantics, recovery limits, platform references and manual acceptance; updated README, architecture, database, roadmap and learning notes. Phase 14B background geofencing and all native/device/release gates remain open.
- Isolated PostgreSQL test databases remaining: 0; repository test cluster stopped and installed PostgreSQL service remains running. Phase 14A remains uncommitted on main over verified 206cd84; user .vscode/ remains excluded. No commit, pull, push, user app shutdown or live provider write was performed by the assistant.
