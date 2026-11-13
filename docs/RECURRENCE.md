# Completion-based recurrence (Phase 12E)

## Use and policy

Open **Recurring tasks** from the task-list toolbar. Choose Device or SyncedAccount, Refresh recurrence, select a live task, choose Daily/Weekly/Monthly, enter an interval from 1 to 365, select an anchor calendar date, review the preview, and Save repeat rule. Reopen a completed task in its normal workspace before enabling recurrence. Not repeating stops the rule while keeping the current due date and consumed occurrence count.

Recurrence uses **one stable task ID and one current occurrence**. Complete occurrence & advance confirms the displayed due date and next date, then moves the same task to its next date. It stays pending. This increment does not create a new task for each date or store completion timestamps/history; focus/time history is Phase 12F. Ordinary completion rejects active repeating tasks with a pointer to this page. Stop recurrence first if you want to finish the task permanently.

- Daily adds interval calendar days; Weekly adds seven times the interval; Monthly adds interval calendar months from the original anchor.
- Monthly dates clamp short months without drift: January 31 -> February 28 (29 in a leap year) -> March 31. Reapplying the identical rule retains its current occurrence.
- A changed rule restarts its calendar at the chosen anchor but never resets the monotonic consumed-occurrence counter. Stopping and re-enabling retain that counter.
- Early/late completion advances exactly one occurrence. Overdue work is not silently skipped or expanded into a backlog; change the anchor deliberately to reschedule.
- Dates are DateOnly calendar values, with no UTC offset or daylight-saving conversion. Weekly means the anchor weekday every N weeks. The device's local day only supplies an initial picker default; server/client advancement uses the same explicit anchor and arithmetic.
- Parent, tags, priority, planning, description, category, and creation time stay on the same task. Children are not cloned, completed, or rescheduled.
- Repeating tasks cannot be prerequisites: their next occurrence stays pending and a stable task-ID edge could otherwise represent the wrong occurrence. Remove incoming links before enabling recurrence, or use a separate one-time milestone. A repeating task's own prerequisites must be completed before it advances; they are not automatically reopened for each occurrence.
- Calendar overflow and counters beyond 1,000,000 reject before a write. Stop/change the rule when its next date cannot fit DateOnly. End dates, custom weekday sets, and arbitrary RRULE strings are outside this rule model.

Device configuration/advance saves immediately in one SQLite transaction. Account changes use the selected API/account's cache; open Synced tasks to upload/download. An account occurrence may advance once offline **after all of that task's queued changes have synced**. Sync/resolve the resulting operation before another completion. Rule edits made while an operation is in flight remain later intent; they never replace its immutable payload. Device tasks are never automatically uploaded.

Selecting another task or refreshing discards an unsaved rule draft. The preview is part of the action review. Conflicts show complete local/server fields; Keep local fields & save repeat rule requires confirmation and rebases the rule at the displayed server occurrence, preserving consumed counts. The preview shows the resulting date. Use server discards local intent. Stale local/server versions or account changes reject the action; other preserved structural changes may conflict again.

## Why no server scheduler

This design needs no timer-triggered task creation: the current future occurrence already exists, and the next date is generated only by an explicit completion transaction. Hangfire, a hosted timer, and another external scheduler would add job lifecycle/duplicate coordination without providing a required action here. No scheduler package/job/service is added. Re-evaluate durable server jobs if later requirements introduce timed materialization, autonomous reminders, or calendar/provider work. Native reminder delivery retains its separate Phase 11 implementation and limitations.

Advancing or changing to a different due date cancels the old due-date reminder through existing committed-write reconciliation. The next occurrence does **not** receive a new reminder automatically; opt in to a new time explicitly. Stopping a rule without changing the due date retains its reminder. Generic notification text and reminder schema remain unchanged.

## Model, idempotency, and ownership

RepeatRule stores Frequency, Interval, AnchorDate. RepeatState stores nullable Rule, monotonic Occurrence, and RuleStart (the counter value at the current anchor). Occurrence is zero-based internally; the screen labels the current occurrence as Occurrence+1. DueDate uses `(Occurrence - RuleStart) * Interval` from the anchor, with an additional factor of seven for weekly rules.

Allowed state transitions preserve the counter while configuring/stopping a rule, or advance exactly one occurrence with the same rule/anchor offset. Arbitrary jumps and backwards counters are rejected. A completion carries the entire expected RepeatState. If its counter is already consumed, retry returns current state without another advance or journal revision; if the rule changed at the same counter, it conflicts for review. Repeated/concurrent requests for the same occurrence advance once.

Server writers retain their per-owner transaction advisory lock through validation, write, journal, and receipt. Sync adds whole-task base revisions and immutable operation receipts. Concurrent offline completions conflict instead of double advancing; reconfiguration against the latest remote counter or Use server converges at the consumed occurrence. Consumed counts are never automatically undone.

SyncRecords retains **RecurrenceFloor** even after task deletion. Restoring the same ID cannot lower that counter. Recurrence repair against a deleted task rebases at this floor, so an old completion cannot advance a restored task twice. The floor is scoped to the authenticated owner and remains with tombstones under the existing retention policy. Backup/restore/pruning must retain counters, receipts, and journal together; no pruning/reset is implemented here.

## API and sync contract

Task responses/sync snapshots add `recurrence`:

```json
{"rule":{"frequency":"Monthly","interval":1,"anchorDate":"2026-01-31"},"occurrence":0,"ruleStart":0}
```

Every supplied state/rule field is required; unknown fields and numeric frequencies are rejected. Frequency is Daily, Weekly, or Monthly. Full task POST/PUT and sync Task data accept nullable Recurrence: omitted/null preserves current state, while an explicit stopped state (Rule=null and RuleStart=Occurrence) stops recurrence without resetting its counter. Active full writes must include the current occurrence's matching dueDate and keep isCompleted=false. Creates start at counter zero; restored IDs respect retained server floors. Prefer the dedicated endpoints for normal use.

```http
PUT /api/tasks/{taskId}/recurrence
Content-Type: application/json

{"rule":{"frequency":"Monthly","interval":1,"anchorDate":"2026-01-31"}}
```

Send `{"rule":null}` to stop; the server retains the current counter. Configuration sets the due date from the chosen anchor/current occurrence. The same rule reapplied after an advance does not reset it.

```http
POST /api/tasks/{taskId}/occurrences/complete
Content-Type: application/json

{"rule":{"frequency":"Monthly","interval":1,"anchorDate":"2026-01-31"},"occurrence":0,"ruleStart":0}
```

The body is the expected current state copied from the task response. Both endpoints require authentication and return the complete task (200), 404 for unavailable/foreign targets, 400 for malformed/unknown/missing fields, or safe 409 for invalid rules/transitions/dependencies/stale occurrence/calendar overflow. Retried consumed completions return current state (200) without advancing. Semantic full-write DTO errors can return 400 before service validation. Client owner IDs/timestamps remain protected.

Sync advertises protocol **6**. Explicit Recurrence input requires 6 before upload; older APIs leave the exact operation ID/payload queued until upgrade/Retry now. Existing earlier feature minimums remain (planning 5, prerequisites/deletion 4, parent 3, tags 2). Absent/null Recurrence stays omitted from legacy request serialization, preserving old receipt bytes. New snapshots include an explicit stopped state when no rule is active. SyncEntry adds a default-omitted RecurrenceFloor; older receipts default it to zero. Coordinated API/client upgrade is required; this does not make old strict binaries compatible with new snapshots/files.

Structural recurrence rejection becomes a receipted whole-task conflict with current server state/reason, preserving local fields. Downloads/acknowledgements reject invalid counter metadata without committing their cursor/outbox changes. Upload ordering waits for queued prerequisite completion before occurrence advance. The queue permits only one unacknowledged advance per task; normal edits/configuration can chain after its receipt.

## Storage and upgrade

| Store | Current version / change |
| --- | --- |
| PostgreSQL | Eighth migration AddTaskRecurrence adds required Tasks.Recurrence JSON text and SyncRecords.RecurrenceFloor bigint default 0 with bounds check |
| todoapp.db3 | Schema 6 adds required Recurrence text defaulting to stopped state at counter 0 |
| sync.db3 | Schema 6 guards recurrence/counter-aware JSON, retaining legacy pending payloads on read |
| views.db3 | Schema 3, unchanged; saved views use the current occurrence's due date and completion state |
| reminders.db3 | Schema 1, unchanged |

Recurrence is an immutable value with explicit EF JSON conversion/value comparison. Task writes, dates, counters, server journal/floor/receipts, and local intent/outbox remain transactional. Existing tasks gain no active rule and retain IDs, owners, fields, dates, timestamps, planning, relationships, queues, receipts, and views. The JSON column does not add database enforcement of calendar/transition/dependency semantics; services enforce them, and administrative SQL must preserve those rules. Unsupported newer/corrupt files fail without reset; supported older files upgrade transactionally. Down removes rule/counter protection and is unsafe for active data.

1. Stop the development API and close older clients. Back up/retain all databases, caches, outboxes, and receipts.
2. With your configured Development connection and JWT signing secret, apply all eight migrations:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
dotnet run --project src/TodoApp.Api --launch-profile https
```

3. Rebuild/run `scripts/Run-Windows.ps1`. Local files upgrade on access. Sign into the same API/account, sync, review conflicts, and Retry now if an older API blocked recurrence-aware operations.

Only generated test databases were migrated during implementation. No user development connection/database, existing Windows PostgreSQL service, or running user app was changed. The API remains Development-only; native TLS/SecureStorage/connectivity/mobile/notification acceptance remains open.

## Hands-on acceptance (pending)

1. Configure daily, weekly, and monthly tasks. Check January 31/leap years, intervals, current/next preview, early/late completion, pending state, and persistence after restart. Confirm no task copies or automatic backlog appear.
2. Retry/double-click the same completion, change a rule while a confirmation is open, stop/re-enable, and check that consumed counts never reset. Try invalid intervals/calendar overflow and confirm no partial edits.
3. Verify ordinary completion/date changes direct repeating tasks to this flow. Confirm title/tags/priority/planning/category/parent stay independent. Test incoming prerequisite rejection and completion of the repeating task's own prerequisites.
4. Sync two replicas, advance the same occurrence offline on both, reconnect, inspect both versions, cancel/confirm recurrence rebase or Use server, then complete the next occurrence. Verify one advance and no duplicates. Confirm further offline completion waits for sync.
5. Test rule edits while a frozen create/advance is queued, loss of a reply, retries, account/API changes during confirmations, and stale repair. Verify old-account rows/drafts clear while its cache remains.
6. Delete/restore a disposable recurring task after an advance; confirm the tombstone floor and an old completion do not reset/advance it again. Preserve counter metadata during any backup/recovery exercise.
7. Check old reminder cancellation after advance/new date, no automatic next reminder, and retention when stopping without a date change. Verify native light/dark/narrow/keyboard/screen-reader/multi-window behavior and compile/test mobile separately.

Automated tests cover pure calendar/transition rules, SQLite upgrades/atomicity, reminder hooks, current client ViewModels, API/JWT/PostgreSQL migrations/ownership/concurrency/receipts/counter floors, and replica convergence. Windows compilation checks native/XAML references; it does not prove native clicks, notification display, device HTTPS, or mobile builds.
