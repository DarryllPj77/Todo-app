# Roadmap

Proceed one phase at a time after review. Phase 12C.2 dependencies are implemented separately from hierarchy, with same-workspace/account and cycle rules, explicit completion/deletion policy, protocol-4 sync, migration, and a dependency screen. Upgrade using DEPENDENCIES.md and complete native acceptance. Next is 12D urgency/importance; remaining Phase 12 increments stay open.
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
- [ ] Evaluate Hangfire versus another scheduler only when recurrence requires server jobs.
- [ ] Each selected feature and adaptive layout has its own completion review.

### Phase 12 increments

- [x] **12A - Custom filters and saved views:** pure query model, literal search, combined completion/priority/category/date rules, deterministic sorting, independent views.db3 version-1 initialization, named create/update/rename/delete, scope isolation, local revision checks, and a usable explorer/source-navigation UI.
- [ ] **12A native acceptance:** follow SAVED_VIEWS.md for actual layout, picker, scrolling, restart, source navigation, and multi-window interaction. Portable tests and Windows compilation are separate checks.
- [x] **12B - Tags:** immutable normalized tag sets, device version-2 upgrade, AddTaskTags server migration, API arrays, sync protocol-2 capability/legacy receipt handling, editors, summaries, and saved-view rules implemented; 25 added cases pass. Native acceptance remains in TAGS.md.
- [x] **12C.1 - Parent hierarchy/subtasks:** optional parent, same-workspace/account validation, cycle/16-level checks, restricted parent deletion, additive migrations, protocol-3 ordering/conflicts, and hierarchy screen implemented. Native acceptance remains in HIERARCHY.md.
- [x] **12C.2 - Dependencies:** separate immutable prerequisite graph, same-account/workspace and cycle checks, completion gate/reopen policy, incoming-delete restriction, transactional migration, protocol-4 ordering/conflicts, and dependency screen implemented. Native acceptance remains in DEPENDENCIES.md.
- [ ] **12D - Urgency/importance:** add Eisenhower semantics through a reviewed model/storage/UI increment.
- [ ] **12E - Recurrence:** define calendar/time-zone, completion, idempotency, and scheduling policy before choosing a server scheduler such as Hangfire.
- [ ] **12F - Focus/time history:** add Pomodoro, TimeSpent, and durable history with explicit timer/lifecycle behavior.
- [ ] **12G - Adaptive workspace:** desktop categories/tasks/details panes and mobile navigation after each feature's data/UI boundaries are stable.

A saved view remains a device-local query definition. Tags remain separate from categories; parent hierarchy and dependencies have separate models/screens. Urgency/importance, recurrence, focus history, adaptive layout, and server subscriptions remain later work. Phase 12 remains open until selected later increments and acceptance reviews are complete.

## Phase 13 — Calendar integration

**Goal:** Add optional calendar planning and time blocking.

**Concepts learned:** OAuth2, Google Calendar API, Microsoft Graph, external sync.

**Tasks:** Implement one provider first, consent/revocation, event mapping, time blocking, then a defined two-way sync policy.

**Expected result:** Predictable optional calendar links without duplication.

**Completion checklist:**

- [ ] Token security, revoked access, time zones, duplicates, and conflicts tested.

## Phase 14 — Location features

**Goal:** Support optional location-triggered reminders.

**Concepts learned:** MAUI geolocation, native geofencing, privacy and battery constraints.

**Tasks:** Request contextual permissions, add geofences on supported platforms, and explain background execution limitations.

**Expected result:** Opt-in location reminders with clear platform behavior.

**Completion checklist:**

- [ ] Permissions, geofence lifecycle, background behavior, and opt-out verified on devices.

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
