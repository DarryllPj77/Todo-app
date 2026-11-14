# Roadmap

Proceed one phase at a time after review. Phase 14B.2 implements bounded Windows background arrivals for one explicitly selected device reminder: protected run journal, transactional opt-out generations, headless COM activation and existing one-attempt delivery. It requires an installed packaged build; the default unpackaged/mobile builds retain manual checks and explain background unavailability. Installed background activation, real device/resource-warning acceptance and earlier Google/mobile/release gates remain open. Next: Phase 14B.3 supported mobile/background lifecycle and device acceptance. See BACKGROUND_GEOFENCES.md.
## Phase 1 — Solution foundation (Windows build/launch verified)

**Goal:** Establish understandable project boundaries and runnable startup shells.

**Concepts learned:** Projects/references, DI, MVVM binding, controllers, test hosting.

**Tasks:** Create five projects, welcome page, liveness endpoint, build filter, docs, and one startup test.

**Expected result:** A verified core foundation and a verified MAUI welcome screen after native tools are provisioned.

**Completion checklist:**

- [x] Structure, references, startup wiring, and requested documentation.
- [x] Core build and API integration test pass.
- [x] Install a Windows MAUI workload and compile the client using the local SDK.
- [x] Run the Windows client and inspect the dark task-list/editor screens (which replace the welcome page).
- [ ] Verify system light-mode switching and other device targets.
- [x] User reviewed the foundation and authorized starting system implementation.

## Phase 2 — Domain models and business rules (implemented)

**Goal:** Define TaskItem, Category, and Priority with small, explicit rules.

**Concepts learned:** Classes, enums, Guid, DateOnly, nullable properties, navigation collections.

**Tasks:** Decide validation and timestamp rules; implement completion changes and category relationship; add focused tests.

**Expected result:** Tested domain behavior independent of UI and databases.

**Completion checklist:**

- [x] Validate titles/priority, optional fields, and completion timestamps.
- [x] Explain decisions and pass meaningful domain tests (21 domain cases plus 1 API startup case).

## Phase 3 — Local CRUD and basic filters (implemented)

**Goal:** Define the smallest useful local task service boundary.

**Concepts learned:** Interfaces, collections, LINQ, cancellation and task-returning contracts.

**Tasks:** Add create/read/update/delete operations and All/Pending/Completed/Due Today/High Priority filtering. Use an in-memory implementation only as a short learning/test step.

**Expected result:** Testable task operations ready for a SQLite implementation.

**Completion checklist:**

- [x] CRUD and completion work through ILocalTaskService and its temporary in-memory implementation.
- [x] Filter tests cover all five selections, local-date boundaries, midnight changes, and missing due dates.
- [x] Missing IDs, rejected edits, category cleanup, cancellation, and result ordering are covered; 36 tests pass across the solution's core projects.

## Phase 4 — SQLite persistence (implemented; full device acceptance pending)

**Goal:** Retain tasks without a network connection.

**Concepts learned:** Local tables, provider I/O limitations, worker tasks, transactions, schema versioning, and detached loading.

**Tasks:** Select compatible SQLite package, map tasks/categories, initialize app-data storage, implement the local service, and verify restart persistence.

**Expected result:** The local data layer survives process restarts.

**Completion checklist:**

- [x] File-based CRUD, relationships, identity/timestamp round trips, and reopening tests pass.
- [x] Initial schema version 1 initializes transactionally; Phase 12B adds the tested 1-to-2 tag upgrade without reset. Unknown newer versions are rejected.
- [x] Database errors propagate; forced failed writes roll back category/task changes together.
- [x] MAUI task ViewModels consume the persistent service using the app-data path.
- [x] Windows native app opens its SQLite-backed task list without a storage error.
- [ ] Complete actual app restart acceptance and verify native packaging on mobile targets.

## Phase 5 — MVVM task UI (implemented; manual acceptance pending)

**Goal:** Make the first usable offline task manager.

**Concepts learned:** INotifyPropertyChanged, ObservableCollection, commands, validation, navigation.

**Tasks:** Add list/editor pages; bind CRUD, completion, due date, priority, category, description, and filters; wire SQLite through DI; keep presentation adaptive and simple.

**Expected result:** A user can manage tasks offline on a supported device.

**Completion checklist:**

- [x] List/editor pages bind create/edit/delete, completion, and all five filters through ViewModels and SQLite.
- [x] Sixteen presentation tests cover validation, canceled edits, confirmations, read/write errors, retry, missing tasks, and duplicate-save prevention.
- [x] Windows MAUI build passes with zero warnings/errors; native dark empty state and editor inspected.
- [ ] Complete the README hands-on checklist for create/edit/toggle/filter/delete and actual app restart.
- [ ] Verify light-mode switching, narrow layouts, accessibility, and mobile sizing on native devices.

## Phase 6 — REST task API (implemented)

**Goal:** Expose clearly defined task operations over HTTP.

**Concepts learned:** DTOs, HTTP semantics, model validation, async controllers/services.

**Tasks:** Implement the five documented task routes with service separation, validation, cancellation, proper status codes, and integration tests. Use a development-only in-memory store until Phase 7.

**Expected result:** A locally testable API contract; no public production deployment yet.

**Completion checklist:**

- [x] All five routes return tested HTTP status codes, including Location on creation and Problem Details for invalid/missing resources.
- [x] Dedicated DTOs validate editable fields and reject unknown/server-managed/navigation fields.
- [x] Concurrent requests, filters, cancellation, ephemeral server state, and Development-only startup are tested.
- [x] Real loopback HTTP create/read/update/delete smoke test passes; the temporary listener was stopped.

## Phase 7 — PostgreSQL and EF Core (implemented; user development connection setup remains)

**Goal:** Persist server task data.

**Concepts learned:** DbContext, entity mapping, relationships, Code First migrations, secrets.

**Tasks:** Select compatible EF/Npgsql versions, register persistence, configure constraints and category deletion, generate/review migration, test against development PostgreSQL.

**Expected result:** API data survives restart with reproducible schema setup.

**Completion checklist:**

- [x] Compatible EF/Npgsql packages, local EF tool, DbContext/Fluent mappings, and generated InitialCreate migration are present.
- [x] Connection comes from secrets/environment configuration; missing connection stops startup without a memory fallback.
- [x] Nine isolated PostgreSQL cases pass: persistence, relationships, constraints, concurrency, rollback, filters, and migrations.
- [x] Existing SQLite/presentation/REST tests still pass; full run is 113 passed, 0 skipped.
- [ ] Configure your own development role/database/connection and apply the included migration using DATABASE.md.

## Phase 8 — Authentication and ownership (server implemented; client/device verification pending)

**Goal:** Protect the server before multi-user integration and sync.

**Concepts learned:** Registration/login, password handling, JWT validation, authorization, secure storage.

**Tasks:** Choose a supported identity approach, add user ownership through migrations, secure all task queries, and store client tokens securely.

**Expected result:** Users can access only their own tasks.

**Completion checklist:**

- [x] Registration/login, password hashing, token expiry/validation, throttling, and cross-user isolation tested against PostgreSQL/JWT middleware.
- [x] Ownership migration preserves anonymous rows without automatically assigning them; category names are per user.
- [x] Signing key comes from validated secrets/configuration; HTTP samples contain placeholders only.
- [x] MAUI SecureTokenStore uses platform SecureStorage with no plaintext/password fallback; Windows compilation passes.
- [ ] Verify actual token storage/restoration/logout on target devices using the Phase 9 UI.
- [ ] Configure the development signing secret and apply AddAuthenticationOwnership locally before using the API.

## Phase 9 — API integration

**Goal:** Add authenticated client/server communication while keeping local operations usable.

**Concepts learned:** HttpClient, serialization, API DTOs, timeouts and cancellation.

**Tasks:** Add login/logout UI and API client services, configurable development endpoints, secure token attachment, expiry/storage failure handling, and account-aware local data boundaries. Do not replace local reads/writes with mandatory network calls or upload unassigned device tasks to whichever account logs in first.

**Expected result:** Controlled authenticated communication with no hidden claim of synchronization.

**Completion checklist:**

- [x] Registration/sign-in/logout, configurable HTTPS origin, per-request bearer attachment, and verified session restoration implemented.
- [x] Separate online server workspace supports CRUD, completion, filters, validation, and delete confirmation without assigning or uploading device tasks.
- [x] Expiry, 401/403, storage failure, offline/timeout/cancellation, stale responses, overlapping login, and uncertain-write handling tested.
- [x] Full suite: 163 passed, 0 skipped, including 19 PostgreSQL facts; native Windows solution build: 0 warnings/errors.
- [ ] Device can reach the development API with trusted HTTPS and complete native account/layout acceptance.
- [ ] Actual platform SecureStorage restoration, expiry, and logout survive app restart; mobile targets remain unverified.

## Phase 10 — Offline synchronization

**Goal:** Reconcile offline work reliably.

**Concepts learned:** Outbox transactions, tombstones, optimistic concurrency, idempotency.

**Tasks:** Define server revisions and download cursor, queue offline creates/edits/deletes, retry with backoff, detect connectivity, and define conflict policy before implementation.

**Expected result:** Local and server task changes converge without silent loss.

**Completion checklist:**

- [x] Shared sync contracts, account-scoped revision journal/receipts, migration, cursor paging, and ordinary REST write integration.
- [x] Separate origin/account SQLite replicas, transactional local intent/outbox/cursor, immutable retries, and deletion tombstones.
- [x] Offline edits/deletes, duplicate uploads, lost replies, stale receipts, account switches, rollback, pagination, legacy bootstrap, and two-replica conflict/deletion convergence tested.
- [x] Synced tasks UI exposes cached CRUD, queue/conflict counts, retry/backoff, Keep mine / Use server, and errors without clearing saved work.
- [x] Page-visible retries and connectivity hints; manual Sync remains available for LAN reachability.
- [ ] Complete native two-device HTTPS/SecureStorage/connectivity/restart/conflict acceptance in SYNCHRONIZATION.md.
- [ ] Before release: retention/reset/export recovery, database-restore reconciliation, cache protection, mobile lifecycle, and performance acceptance.

## Phase 11 — Notifications

**Goal:** Deliver useful due-date reminders.

**Concepts learned:** Local notifications, permissions, platform-specific scheduling.

**Tasks:** Add explicit reminder time/time-zone semantics, schedule/cancel notifications, handle denied permissions and edited tasks.

**Expected result:** Reminders work within each platform's documented limits.

**Completion checklist:**

- [x] Explicit opt-in reminder time, fixed UTC instant/original zone, DST gap/repeat rejection, and durable per-device settings.
- [x] Generic notification payloads, stable IDs, permission-denial recovery, replacement/cancellation, completion/deletion/due-date/account handling, and nearest-32 policy.
- [x] Windows running-app notification adapter, Android inexact alarm/channel/permission/boot receiver, and Apple local-request/delegate adapters supplied without new packages.
- [x] Twenty-six policy/storage/ViewModel regression cases pass; complete suite is 216 passed, 0 skipped; Windows/XAML build has 0 warnings/errors.
- [ ] Actual native scheduling/display/cancellation, time changes, permission denial, restart, and account switching verified on Windows/mobile devices.
- [ ] Compile and validate Android/iOS/Mac Catalyst adapters with their native toolchains.
- [ ] Durable Windows closed-app delivery/packaging remains a platform milestone; current development build explicitly requires the app running.

## Phase 12 — Advanced tasks and adaptive desktop UI

**Goal:** Add organization features in small reviewed increments.

**Concepts learned:** Self-references, separate dependency graphs, recurrence rules, richer LINQ queries.

**Tasks:** Incrementally add tags/custom filters/saved views; nested tasks; separate task dependencies with cycle handling; Eisenhower urgency/importance; recurring daily/weekly/monthly tasks; Pomodoro sessions, TimeSpent, and history; desktop three-pane/mobile navigation layouts.

**Expected result:** Each feature has a tested model, migration, and usable UI before another is added.

**Completion checklist:**

- [x] Parent hierarchy and dependencies are separately modeled and validated; native acceptance remains in HIERARCHY.md/DEPENDENCIES.md.
- [x] Evaluated scheduler need: completion-based recurrence uses explicit transactions and needs no server job. Revisit Hangfire/hosted scheduling if timed generation is added; see RECURRENCE.md.
- [ ] Each selected feature and adaptive layout has its own completion review.

### Phase 12 increments

- [x] **12A - Custom filters and saved views:** pure query model, literal search, combined completion/priority/category/date rules, deterministic sorting, independent views.db3 version-1 initialization, named create/update/rename/delete, scope isolation, local revision checks, and a usable explorer/source-navigation UI.
- [ ] **12A native acceptance:** follow SAVED_VIEWS.md for actual layout, picker, scrolling, restart, source navigation, and multi-window interaction. Portable tests and Windows compilation are separate checks.
- [x] **12B - Tags:** immutable normalized tag sets, device version-2 upgrade, AddTaskTags server migration, API arrays, sync protocol-2 capability/legacy receipt handling, editors, summaries, and saved-view rules implemented; 25 added cases pass. Native acceptance remains in TAGS.md.
- [x] **12C.1 - Parent hierarchy/subtasks:** optional parent, same-workspace/account validation, cycle/16-level checks, restricted parent deletion, additive migrations, protocol-3 ordering/conflicts, and hierarchy screen implemented. Native acceptance remains in HIERARCHY.md.
- [x] **12C.2 - Dependencies:** separate immutable prerequisite graph, same-account/workspace and cycle checks, completion gate/reopen policy, incoming-delete restriction, transactional migration, protocol-4 ordering/conflicts, and dependency screen implemented. Native acceptance remains in DEPENDENCIES.md.
- [x] **12D - Urgency/importance:** manual four-quadrant classification plus Unclassified, independent priority/due date, transactional upgrades, strict API enum, protocol-5 queue/receipt compatibility, scoped planning UI/conflict review, summaries, and saved-view rules implemented. Native acceptance remains in EISENHOWER.md.
- [x] **12E - Recurrence:** date-only daily/weekly/monthly interval rules, anchored short-month handling, same-task completion/monotonic counters, idempotent retries/tombstone floor, dependency policy, transactional upgrades, protocol-6 queue/receipt/conflict handling, reminder reconciliation, and Recurring tasks UI implemented. Native acceptance remains in RECURRENCE.md; no server scheduler is needed for this completion-based model.
- [x] **12F - Focus/time history:** configurable Pomodoro focus/short/long breaks, pause/resume/manual transitions, monotonic elapsed time, durable focus.db3 ledger/checkpoints/recovery, one-session/process guards, scoped derived TimeSpent/history, task/recurrence/account interruption, and Focus & history UI implemented. Data stays local; native lifecycle/timer acceptance remains in FOCUS.md.
- [x] **12G - Adaptive workspace implementation:** device categories/tasks/read-only details panes at 1180 logical pixels, compact Tasks/Details navigation, literal search, selection retention, and consolidated Tools navigation. Six new behavior cases pass; no data/API migration. See ADAPTIVE_WORKSPACE.md.
- [ ] **12G native acceptance:** wide Windows preview/category selection/details checked; compact native inspection was stopped by the user. Mobile, accessibility, editing and full acceptance remain open.

A saved view remains a device-local query definition. Focus history is also device-local and scoped independently of task sync; TimeSpent is derived from its ledger. Recurrence advances one stable task after explicit completion. Adaptive layout is implemented; remaining native/release acceptance stays open. Phase 12 is not yet complete.

## Phase 13 — Calendar integration

**Goal:** Add optional calendar planning and time blocking.

**Concepts learned:** OAuth2, Google Calendar API, Microsoft Graph, external sync.

**Tasks:** Implement one provider first, consent/revocation, event mapping, time blocking, then a defined two-way sync policy.

**Expected result:** Predictable optional calendar links without duplication.

**Completion checklist:**

- [ ] Token security, revoked access, time zones, duplicates, and conflicts tested.

### Phase 13 increments

- [x] **13A - Local planning and file exchange:** task-linked scoped calendar.db3 version-1 blocks, fixed UTC/original zone semantics, DST validation, transactional nonoverlap, revision checks, snapshot retention, draft-aware MAUI page and selected-block RFC 5545 export. Twenty-one new portable cases pass. Google Calendar is the chosen first provider.
- [ ] **13A native acceptance:** actual screen, share dialog/file delivery, manual Google import, mobile build/FileProvider/iPad anchor, accessibility and account/lifecycle checks; see CALENDAR.md.
- [x] **13B Windows implementation - Google OAuth:** configurable Desktop client, system-browser IPv4 loopback PKCE/state/cancellation, protected Todo-account/origin/Google-subject binding, identity checks, refresh rotation, fail-closed restoration markers and explicit disconnect/revocation. Twenty-six new portable/loopback cases pass; see GOOGLE_CALENDAR.md.
- [ ] **13B provider/native/mobile acceptance:** real configured Google consent and token lifecycle, SecureStorage/restart, browser/firewall/account-race checks and supported mobile flows remain open. Mobile/preview authorization is disabled.
- [x] **13C Windows implementation - Explicit Google event links:** selected owned calendars, schema-2 calendar journal, reserved UUID event identity/private ownership markers, captured snapshots, revision guards, conditional ETag updates/deletes, confirmed explicit writes/retries and uncertain-write reconciliation. Twenty-three new cases pass; see GOOGLE_EVENT_LINKS.md.
- [ ] **13C provider/native acceptance:** actual Google listing/writes/restart/retry/conditional conflict behavior, two-window/session races and native layout remain open. No live Google account was modified during implementation.
- [x] **13D.1 - Reviewed reconciliation:** schema-3 persisted Google/local reviews, title/time field policy, explicit adoption/conditional local push/deletion acceptance/local detach, stale review/session guards, transactional overlap/CAS checks, orphan and exact-UTC/DST handling. Twenty-five new review cases pass; see GOOGLE_RECONCILIATION.md.
- [ ] **13D.1 provider/native acceptance:** real Google review/resolution/races, native selection/confirmations/accessibility and earlier platform/storage acceptance remain open.
- [x] **13D.2 - Incremental cursors and opt-in monitoring:** schema-4 bound cursor/opt-in profiles, bounded full/incremental pages, atomic cursor/notices with cohort CAS, 410 full reset, backoff/Retry-After/pause, authoritative local opt-out and page-active read-only checks. Twenty-four new cases pass; see GOOGLE_MONITORING.md.
- [ ] **13D.2 provider/native acceptance:** actual Google pagination/reset/access/rate-limit behavior, page/window/network lifecycle, privacy/unlinked payload handling and earlier gates remain open. No automatic provider publishing/adoption/deletion is enabled.

ICS import remains a separate snapshot exchange and is not adopted as a provider link. OAuth and explicit provider links are implemented separately; task completion and automatic synchronization are not inferred. Provider-to-local block adoption now requires an explicit reviewed choice. Phase 13 remains open.

## Phase 14 — Location features

**Goal:** Support optional location-triggered reminders.

**Concepts learned:** MAUI geolocation, native geofencing, privacy and battery constraints.

**Tasks:** Request contextual permissions, add geofences on supported platforms, and explain background execution limitations.

**Expected result:** Opt-in location reminders with clear platform behavior.

**Completion checklist:**

- [ ] Permissions, geofence lifecycle, background behavior, and opt-out verified on devices.

### Phase 14 increments

- [x] **14A implementation - Manual foreground arrival checks:** task-linked encrypted local places, contextual one-shot location requests, accuracy/freshness and outside-before-inside rules, one-attempt state, explicit rearm/disable/delete, generic notifications, scope/session/CAS guards and a MAUI management page. Forty portable location cases pass; see [LOCATION_REMINDERS.md](LOCATION_REMINDERS.md).
- [ ] **14A native acceptance:** actual GPS/OS permission/notification behavior, encrypted key/restart/recovery, page/window/account races, accessibility and Android/Apple builds/devices remain open. No background listener or geofence is installed.
- [x] **14B.1 implementation - Packaged Windows native sessions:** contextual access, two conservative regions per place, ten-place/thirty-minute limits, single-window/cross-process ownership, exact session/arm callbacks, metadata-only reconciliation, explicit stop/retry cleanup and optional unsigned package build. Thirty-five new portable cases pass; see [NATIVE_GEOFENCES.md](NATIVE_GEOFENCES.md).
- [ ] **14B.1 packaged/device acceptance:** signing/install, actual permission/geofence/dwell/expiry/notification behavior, multi-process/crash cleanup, accessibility and package PRI249/PRI263 resource/locale warnings remain open. Default unpackaged/mobile/preview builds do not enable native sessions.
- [x] **14B.2 Windows implementation - Bounded background arrivals:** one explicit device reminder, encrypted schema-1 run journal, key-independent transactional Stop generations, thirty-minute native lifetime, Windows App SDK LocationTrigger/COM headless entry, exact reports/arm validation, shared lease, generic guarded notifications and cleanup/retry controls. See [BACKGROUND_GEOFENCES.md](BACKGROUND_GEOFENCES.md).
- [ ] **14B.2 installed/device acceptance:** signed deployment, actual COM/headless activation after UI closure, protected-key access, native permission/cancellation/expiry/throttling, toast identity/clicks, multi-process races and package resource warnings remain open. No native activation or real coordinates were operated during implementation.
- [ ] **14B.3 - Supported mobile/background lifecycle:** implement and verify selected mobile adapters, contextual permissions, quota/battery behavior and restart/boot policy. No automatic rearm or boot registration is enabled by the Windows increment.

Phase 14 remains open until supported native/background behavior and opt-out are verified on devices. Earlier Phase 13 provider/native gates remain open separately.

## Phase 15 — Universal quick capture

**Goal:** Capture tasks quickly using platform-appropriate entry points.

**Concepts learned:** Windows global shortcut, browser extension boundaries, mobile widgets.

**Tasks:** Implement one capture surface at a time, reuse local validation, and queue captured tasks for sync.

**Expected result:** Quick capture works without bypassing normal data/security rules.

**Completion checklist:**

- [ ] Each enabled capture surface handles offline work and authentication safely.

## Phase 16 — Release hardening

**Goal:** Prepare selected features for reliable release.

**Concepts learned:** Accessibility, deployment, observability, backup/restore, automated builds.

**Tasks:** Expand useful coverage throughout development; here complete platform regression, performance checks, migration recovery, packaging/signing, and release documentation.

**Expected result:** Reproducible release with known limitations and recovery procedures.

**Completion checklist:**

- [ ] Release builds/tests, device acceptance, accessibility, and database recovery pass.
- [ ] Deployment/security/configuration instructions match the shipped application.

SQLite precedes the task UI so the first usable UI is persistent. Authentication precedes integration/sync to avoid designing a multi-user server without ownership. Testing begins in Phase 1 and accompanies every phase; Phase 16 is the final hardening gate, not the first testing phase.
