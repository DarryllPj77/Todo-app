# Task dependencies (Phase 12C.2)

## Use and policy

Open **Dependencies** from the task-list toolbar. Choose Device or SyncedAccount, refresh, select the task that needs prerequisites, choose another task, and Add selected prerequisite. Remove selected link or Clear all prerequisites edits the draft. **Save prerequisites** applies the complete draft set. Selecting another task or refreshing reloads saved links and discards unsaved link selections. Normal task editors continue editing other task fields.

Device writes save immediately. Account writes queue locally; open Synced tasks to upload/download. Sign in and sync first for account data. Device tasks remain separate and are never automatically uploaded.

- A task may require up to **32 submitted prerequisite IDs**; duplicate IDs collapse into a sorted immutable set. Empty IDs are invalid.
- Every prerequisite must be a live task in the same workspace/account. Self links, missing/foreign tasks, and directed cycles are rejected.
- A pending task can become completed only when all its direct prerequisites are completed. Adding/changing links on a completed task also requires every resulting prerequisite to be completed.
- Reopening a prerequisite does **not** undo completed dependents. Their ordinary field edits remain allowed. Reopen a dependent and its next completion transition is checked again. No automatic completion or scheduling cascade runs.
- Remove incoming links from all live dependents before deleting a prerequisite, even if those dependents are completed. Deleting a dependent removes its own outgoing links with it.
- Parent hierarchy is independent. Parent/child nesting neither creates nor clears prerequisites, and its 16-level limit does not apply to dependency paths.
- The screen shows unfinished/missing prerequisite counts and readiness based on its current device/cache snapshot. Sync before treating cached status as server state. Missing cache rows count as blockers; server checks remain authoritative.

## Model and validation

TaskPrerequisites is an immutable, canonical, value-equal collection with an explicit JSON GUID-array converter. TaskItem owns its outgoing set. DependencyNode/TaskDependencies provide graph checks independent of hierarchy. Iterative traversal uses active/finished sets, rejects cycles, supports shared prerequisites (a diamond graph), and avoids recursion depth failures. Validation follows reachable prerequisites from the changed task, allowing unrelated incomplete cache pages to remain readable.

The completion rule is a transition gate, not a permanent invariant that every completed task always has completed prerequisites. This makes reopening explicit without silently changing other tasks. Parent-only and dependency-only operations preserve unrelated fields; effective relationship changes update edit time while retaining creation time.

Device writes validate/read/write inside one SQLite transaction. Server writes use the existing per-owner transaction advisory lock through validation, write, and revision journal. Concurrent opposite edges cannot both commit a cycle. Structural rejection precedes task/category mutation. Portable server memory substitutes apply the same rules under their existing gate.

## API and sync contract

Task responses add `prerequisites`, a GUID array. Full task POST/PUT and sync Task data accept the same field. **Omitted/null prerequisites preserve existing links on update; `[]` explicitly clears them.** Creates default to no prerequisites. Other fields retain their existing full-PUT behavior.

The authenticated relationship-only endpoint accepts a raw array:

```http
PUT /api/tasks/{taskId}/prerequisites
Content-Type: application/json

["00000000-0000-0000-0000-000000000001"]
```

Use `[]` to clear. Success returns the complete task (200); an unavailable target returns 404; invalid graph/completion/deletion returns safe 409 Problem Details. Malformed values, a null route body, objects instead of arrays, empty IDs, and over 32 submitted IDs return 400. A foreign prerequisite and nonexistent prerequisite return the same safe error; ownership comes from the bearer token.

Dependencies introduced protocol **4**; planning introduced **5** and current recurrence snapshots advertise **6** (RECURRENCE.md). Any operation explicitly carrying prerequisites, and every deletion (which needs incoming-link validation), requires 4; parent-aware operations require 3 and tagged operations require 2. Capability checks run before writes, retain blocked operations, and Retry now uses their original IDs after the API upgrade. Null Prerequisites stays omitted during serialization, preserving pre-tag, tagged, and hierarchy-era request receipt bytes. New snapshots include an explicit empty set when there are no links.

Uploads wait for queued new prerequisites to exist remotely, and for queued prerequisite completion before dependent completion. Known server incoming links must be removed/deleted remotely before prerequisite deletion uploads. Each pass is bounded; related work or newer remote changes may need another Sync. Ordering never replaces server validation. Structural invalidity produces a receipted whole-task conflict with current server state/reason, preserving local intent.

In Dependencies, review both full versions, edit the prerequisite draft, and choose **Keep local fields & save prerequisites** with confirmation. It queues a fresh operation against the displayed revision, guarded by the expected local snapshot. Stale local/server versions or account changes reject the action. Use server discards that task's local intent. Other invalid fields/parent intent can still cause a later conflict; the UI never automatically merges whole-task edits or replaces an immutable in-flight operation.

## Storage and upgrade

| Store | Current version / change |
| --- | --- |
| PostgreSQL | Sixth migration AddTaskDependencies adds required Tasks.Prerequisites TEXT, default `[]` |
| todoapp.db3 | Current schema 6; the 3-to-4 dependency upgrade adds required Prerequisites TEXT, default `[]`, transactionally after hierarchy |
| sync.db3 | Current schema 6 adds recurrence after planning/prerequisite-aware JSON; old pending payloads are not rewritten on read |
| views.db3 | Current schema 3 adds quadrant rules; saved views remain flat, without a dependency filter |
| reminders.db3 | Schema 1, unchanged; dependency-only edits retain reminder time |

The bounded outgoing sets use JSON text with EF conversion/value comparison, matching immutable whole-task sync snapshots. There is no separate dependency table or per-edge sync revision in this increment. Membership, account ownership, cycles, incoming-delete restrictions, and completion gates are enforced by services inside transactions; **the JSON column has no per-ID database foreign keys**. Administrative SQL can violate these rules and must be reviewed. Normal category and parent FKs remain intact. A normalized edge table/index would be a separate migration if server-side graph querying/scale warrants it; current queries materialize this small workspace.

Existing tasks receive empty prerequisites. IDs, parents, tags, owners, categories, timestamps, saved views, reminders, receipts, and queued requests remain. Do not downgrade active files: older clients reject current schema 6, and migration Down removes all prerequisite sets. Upgrade API/client together; preserving old queued bytes does not make old strict binaries compatible with new snapshots.

1. Stop the development API and close older clients. Retain/back up existing databases/files and queues; do not delete caches to upgrade.
2. With your configured Development connection and JWT signing secret, apply all eight migrations:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
dotnet run --project src/TodoApp.Api --launch-profile https
```

3. Rebuild/run `scripts/Run-Windows.ps1`. Local files upgrade on access. Sign into the same API/account, sync, review conflicts, and Retry now if an older API previously blocked an upload.

Implementation verification migrates only generated test databases; your development database has not been migrated automatically. Existing TLS, SecureStorage, connectivity, native notifications, and mobile acceptance gates remain open.

## Hands-on acceptance (pending)

1. Create A and B; make B require A. Try completing B, finish A, complete B, then reopen A. Confirm B remains completed; reopen B and verify completion is blocked again. Check unrelated fields/parents/tags.
2. Add several prerequisites, duplicate titles, a diamond graph, a long path, 32 links, self/cyclic links, and missing tasks. Check save errors preserve the draft. Remove links/clear, refresh, restart, and verify retention.
3. Try deleting A while B references it, including completed B. Remove B's link, then delete A. Confirm B remains and parent restrictions still apply independently.
4. Use two replicas over trusted HTTPS: queue offline prerequisite/dependent creates and completion, restart, and sync. Queue link removal then prerequisite deletion and verify convergence without duplicate/endless operations.
5. Make opposite edges offline on two replicas. Sync, review both complete versions, cancel/confirm a repaired prerequisite set, try Use server, and switch accounts during confirmation. Local intent must remain and account rows must clear.
6. Upgrade disposable version-3 files with tags/parents/pending requests. Try an older API: prerequisite-aware writes must remain queued, then Retry now succeeds after upgrading.
7. Check light/dark/narrow layouts, keyboard/screen readers, long conflict details, native restart, multiple windows, and mobile targets. Portable tests and Windows compilation do not prove native interaction or device HTTPS.

## Learning focus

Trace an outgoing immutable set through draft, graph validation, transaction, outbox, receipt, and acknowledgement. Compare a tree's single parent with a graph's multiple prerequisites. Understand why immutable equality matters after JSON reload, why a completion gate differs from cascading state, and why ordering related writes still needs conflict review.

Phase 12E disallows active repeating tasks as prerequisites because their stable task ID immediately represents the next pending occurrence after completion. Remove incoming links before enabling recurrence, or use a separate one-time milestone. A repeating task may have one-time prerequisites; all must be complete before its occurrence advances, and they are not automatically reopened. See RECURRENCE.md.
