# Roadmap

Proceed one phase at a time after review. Phase 6 task REST endpoints are implemented and 103 tests pass, including 32 new API cases. Windows MAUI build/launch was verified in Phase 5; its remaining manual acceptance checks are still open. The next implementation phase is PostgreSQL/EF Core, not client/server synchronization.

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
- [x] Schema version 1 initializes transactionally and reopens without data reset; unknown newer versions are rejected.
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

## Phase 7 — PostgreSQL and EF Core

**Goal:** Persist server task data.

**Concepts learned:** DbContext, entity mapping, relationships, Code First migrations, secrets.

**Tasks:** Select compatible EF/Npgsql versions, register persistence, configure constraints and category deletion, generate/review migration, test against development PostgreSQL.

**Expected result:** API data survives restart with reproducible schema setup.

**Completion checklist:**

- [ ] Documented migration process and secret-based configuration work.
- [ ] Real PostgreSQL CRUD/relationship checks pass.

## Phase 8 — Authentication and ownership

**Goal:** Protect the server before multi-user integration and sync.

**Concepts learned:** Registration/login, password handling, JWT validation, authorization, secure storage.

**Tasks:** Choose a supported identity approach, add user ownership through migrations, secure all task queries, and store client tokens securely.

**Expected result:** Users can access only their own tasks.

**Completion checklist:**

- [ ] Registration/login/token lifecycle and cross-user isolation tested.
- [ ] No secrets/tokens appear in source or logs; device token storage reviewed.

## Phase 9 — API integration

**Goal:** Add authenticated client/server communication while keeping local operations usable.

**Concepts learned:** HttpClient, serialization, API DTOs, timeouts and cancellation.

**Tasks:** Add API client services, configurable development endpoints, secure token attachment, and clear failure handling. Do not replace local reads/writes with mandatory network calls.

**Expected result:** Controlled authenticated communication with no hidden claim of synchronization.

**Completion checklist:**

- [ ] Device can reach development API with correct transport configuration.
- [ ] Offline, unauthorized, and timeout behavior is understandable.

## Phase 10 — Offline synchronization

**Goal:** Reconcile offline work reliably.

**Concepts learned:** Outbox transactions, tombstones, optimistic concurrency, idempotency.

**Tasks:** Define server revisions and download cursor, queue offline creates/edits/deletes, retry with backoff, detect connectivity, and define conflict policy before implementation.

**Expected result:** Local and server task changes converge without silent loss.

**Completion checklist:**

- [ ] Offline edits/deletes, repeated uploads, interrupted sync, and conflicts tested.
- [ ] Sync status and recoverable error paths visible to users.

## Phase 11 — Notifications

**Goal:** Deliver useful due-date reminders.

**Concepts learned:** Local notifications, permissions, platform-specific scheduling.

**Tasks:** Add explicit reminder time/time-zone semantics, schedule/cancel notifications, handle denied permissions and edited tasks.

**Expected result:** Reminders work within each platform's documented limits.

**Completion checklist:**

- [ ] Scheduling, cancellation, time changes, and permission denial verified on devices.

## Phase 12 — Advanced tasks and adaptive desktop UI

**Goal:** Add organization features in small reviewed increments.

**Concepts learned:** Self-references, separate dependency graphs, recurrence rules, richer LINQ queries.

**Tasks:** Incrementally add tags/custom filters/saved views; nested tasks; separate task dependencies with cycle handling; Eisenhower urgency/importance; recurring daily/weekly/monthly tasks; Pomodoro sessions, TimeSpent, and history; desktop three-pane/mobile navigation layouts.

**Expected result:** Each feature has a tested model, migration, and usable UI before another is added.

**Completion checklist:**

- [ ] Parent hierarchy and dependencies are separately modeled and validated.
- [ ] Evaluate Hangfire versus another scheduler only when recurrence requires server jobs.
- [ ] Each selected feature and adaptive layout has its own completion review.

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
