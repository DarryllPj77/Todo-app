# Urgency and importance (Phase 12D)

## Use and meaning

Open **Urgency & importance** from the task-list toolbar. Choose Device or SyncedAccount, Refresh planning, select a task and classification, then Save classification. Device changes save immediately; account changes queue locally. Open Synced tasks to upload/download account changes. Sign in and sync first for account tasks; device tasks remain separate.

Urgency and importance are **manual planning choices**, independent of priority, due date, completion, tags, category, hierarchy, dependencies, and reminders. An overdue or High-priority task does not classify itself. Passing midnight does not move tasks between quadrants. The four suggestions organize decisions; they do not run scheduling, delegation, deletion, or external integrations.

| Stored/API quadrant | Urgent | Important | Suggestion |
| --- | --- | --- | --- |
| Unclassified | Unknown | Unknown | Review and classify |
| UrgentImportant | Yes | Yes | Do first |
| ImportantNotUrgent | No | Yes | Schedule |
| UrgentNotImportant | Yes | No | Consider delegating |
| Neither | No | No | Reconsider |

**Unclassified is distinct from Neither.** Existing/new tasks start Unclassified. Selecting it explicitly clears a classification; it does not delete the task. A single enum stores the valid combinations with no partially specified axes. Eisenhower.FromFlags maps two explicit Boolean choices; IsUrgent/IsImportant return null for unknown Unclassified. No priority remapping or date heuristic runs.

The page lists tasks by quadrant, then title/ID, with pending-live counts for every quadrant. Completed tasks are hidden unless Include completed is selected; conflicts remain visible for review. Counts cover all pending live tasks, independently of the display filter. Deletion intent/conflict rows can appear outside those counts. Selecting another task, changing a display filter, or refreshing reloads saved classification and discards an unsaved selection.

## Saved views and field preservation

Saved views add an urgency/importance picker: All quadrants, Unclassified, and the four explicit quadrants. A selected rule combines with existing search/completion/priority/category/date/tag rules using AND. Null means Any; explicit Unclassified matches only unclassified tasks. Definitions remain local preferences scoped to device or canonical API/account. Applying/renaming/deleting a view does not write tasks or queues. Matrix display filtering is temporary; save reusable rules in Saved views.

Ordinary task editors preserve an omitted classification. The planning-only service/API changes only Quadrant and effective edit time; creation time and all other task fields remain. Hierarchy/dependency changes and conflict detail snapshots retain classification. Dependency completion gates still apply independently. Planning-only edits reconcile reminders through existing committed-write hooks but do not change their time or payload.

## API and synchronization

Task responses include string `quadrant`. Full task POST/PUT and sync Task data accept the same enum. **Omitted/null quadrant preserves classification on updates; explicit `"Unclassified"` clears it.** Creates with no classification start Unclassified. Other full-PUT fields retain existing replacement/defaulting semantics.

The authenticated planning-only endpoint accepts a raw JSON enum string:

```http
PUT /api/tasks/{taskId}/quadrant
Content-Type: application/json

"ImportantNotUrgent"
```

Success returns the complete task (200); an unavailable/foreign target returns 404. Null/missing route bodies, objects/arrays, undefined strings, JSON numbers, and numeric strings return 400. The full-write DTO still rejects unknown/server-owned fields. Account identity comes from the bearer token. The existing per-owner transaction lock keeps write/journal atomic; sync whole-task revision checks choose one winner for competing edits.

Planning introduced protocol **5**; current recurrence snapshots advertise **6** (RECURRENCE.md). Explicit Quadrant input requires 5 before upload, even for Unclassified. Older APIs block the operation with its original ID/payload retained; Retry now reuses it after upgrade. Prerequisite writes/deletions require 4, parent writes 3, and tags 2. Null Quadrant is omitted from legacy mutation serialization, preserving receipt request bytes from all earlier phases. New snapshots include explicit Unclassified where applicable.

Classification uses whole-task conflicts, without automatic field merging. The planning page shows both complete versions. **Keep local fields & save classification** requires confirmation and creates a fresh operation against the displayed remote revision, with expected-local checks; stale data/account changes reject the action. Use server discards that task's local intent. Preserved parent/dependency changes can still conflict later. Normal edits never replace an immutable in-flight operation; later local intent becomes the next operation after acknowledgement.

## Storage and upgrade

| Store | Current version / change |
| --- | --- |
| PostgreSQL | Seventh migration AddTaskPlanning adds required integer Tasks.Quadrant default 0 with 0..4 check |
| todoapp.db3 | Current schema 6; the planning upgrade adds equivalent required INTEGER column/default/check after dependency upgrade |
| sync.db3 | Current schema 6 adds recurrence after quadrant-aware JSON; old pending payloads are retained without rewrite on read |
| views.db3 | Schema 3 adds quadrant-aware definition JSON; old definitions/IDs/revisions remain without rewrite on read |
| reminders.db3 | Schema 1, unchanged |

Enum values are stable: Unclassified=0, UrgentImportant=1, ImportantNotUrgent=2, UrgentNotImportant=3, Neither=4. SQL checks reject unsupported values; the shared strict JSON converter accepts named values and rejects integers. Domain setters validate casts and retain UpdatedAt for no-op assignments. Restore retains stored timestamps. Unknown newer files fail without reset.

Existing task IDs, ownership, parents, prerequisites, tags, categories, completion, dates, timestamps, queues, receipts, saved definitions, and reminders remain. No new package or service dependency is added. No user development database is migrated automatically. Down removes all classifications; older binaries cannot safely share new payloads/files. Upgrade API/client together and retain backups/queues rather than deleting caches.

1. Stop the development API and close older client builds. Back up/retain existing databases and local files.
2. Use your configured Development connection and JWT signing secret; apply all eight migrations:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
dotnet run --project src/TodoApp.Api --launch-profile https
```

3. Rebuild/run `scripts/Run-Windows.ps1`. Local files upgrade on access. Sign into the same API/account, sync, inspect conflicts, and Retry now after upgrading an API that blocked quadrant-aware uploads.

Implementation verification migrates generated test databases only. API startup remains Development-only without automatic migrations. Existing TLS/SecureStorage/connectivity/mobile/notification acceptance gates remain open.

## Hands-on acceptance (pending)

1. Create tasks with different priorities/due dates. Confirm they start Unclassified. Assign all four quadrants, clear one to Unclassified, refresh, close/reopen, and verify retention and independent task fields.
2. Include completed tasks, inspect pending counts, filter each quadrant, and check duplicate-title ordering. Verify filters/selection do not save until Save classification.
3. Save quadrant rules combined with existing rules. Restart and reapply; compare Any versus Unclassified versus Neither. Switch device/account/API and verify separate definitions/results.
4. Classify cached account tasks offline, edit again before a queued create is acknowledged, restart, and sync two replicas over trusted HTTPS. Verify later classification/clear converges without duplicate or endless uploads.
5. Change the same task on two replicas, review both complete versions, cancel/confirm Keep local, try Use server, and switch accounts during confirmation. Stale actions must not write the wrong account.
6. Upgrade disposable schema-4 task/cache files and schema-2 views with existing fields/pending requests. Confirm no reset/rewrite. Try an older API, then upgrade and Retry now.
7. Check planning-only reminder retention, dependency completion gates, parent/tag retention, native keyboard/screen-reader labels, light/dark/narrow layout, multiple windows, and mobile targets. Compilation/portable tests do not prove native clicks or device HTTPS.

## Learning focus

Compare independent concepts before deriving one from another. A five-state enum preserves valid urgency/importance combinations and distinguishes unknown from false. Follow nullable input (preserve) versus explicit Unclassified (clear) through JSON, storage, old receipt replay, immutable queues, and whole-task conflict review. Saved queries read classification without changing it.
