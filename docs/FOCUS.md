# Focus sessions, TimeSpent, and local history (Phase 12F)

## Use and scope

Rebuild the Windows client and open **Focus & history** from the task-list toolbar. Select Device or SyncedAccount, Refresh, choose a pending task, and Start focus. Defaults are 25 minutes of focus, a five-minute short break, and a 15-minute long break. Focus accepts 1-180 minutes; each break accepts 1-60. Starting any session saves those durations for the selected workspace. Editing fields does not change an active session; Refresh reloads saved durations.

Pause freezes the timer; Resume starts counting again. Stop saves the tracked portion as a stopped session. A completed focus block suggests a break; after four completed focus blocks since the last completed long break, the screen suggests a long break. Breaks start manually. Finishing a focus block does not complete/advance the task or recurrence, and does not schedule a reminder, OS notification, or another session. The page displays completion/history; no timer alarm delivery is claimed.

This increment keeps focus data **local to this device**. Device history and each canonical API origin/account's history are separate. Account tasks come from that account's current cache, so sign in and sync first to choose them. Focus works offline while the selected saved session is valid. No focus rows, totals, durations, or history are uploaded; there is no new API endpoint, server migration, or sync protocol. Existing recurrence/task behavior remains unchanged.

## Time accounting

- One session may be running or paused across all workspaces/windows on this device. Any window in its workspace can control that shared session. Another workspace shows a generic busy message without its private task title/history.
- The heartbeat runs independently of the Focus page. Navigating away or using another desktop app can keep counting while this app process keeps ticking. Pause/Stop explicitly when you stop focusing.
- Elapsed time comes from TimeProvider's monotonic timestamp, not subtraction of wall-clock UTC times. Changing the clock cannot add hours or make credited time negative. UTC start/update/end timestamps are recorded separately and clamped against backwards clock changes; sequence numbers order history even if the wall clock jumps.
- The runtime observes roughly once per second and saves a durable checkpoint at least every five observed seconds, plus pause/resume/stop/completion transitions. A crash can lose the unsaved tail; it never credits the closed-app gap.
- A gap over ten seconds, a backwards monotonic reading, suspension, or shutdown interruption ends the session conservatively. A resumed/restarted app does not auto-start another session. New starts are blocked while the app is marked suspended.
- When all app windows report Stopped/Destroying, the runtime attempts to save/interrupt the active session. Desktop deactivation alone does not stop it. Actual OS lifecycle delivery, minimization, sleep, and mobile suspension remain native acceptance items; a missed event is also bounded by heartbeat-gap/restart recovery.
- Startup/lease takeover changes an abandoned Running/Paused record to Interrupted at its last saved checkpoint. It does not infer duration from its UTC start time or restore an old countdown automatically.
- A running task is checked at checkpoints. Completion/deletion or a changed recurrence state interrupts tracking and discards the unchecked tail since the last persisted task check. A paused task is rechecked before resuming. Rename/other metadata edits retain the session's captured title and occurrence state.
- Logout/account/origin change or expiry interrupts an account session and clears account-facing rows/drafts. Device sessions are independent of sign-in changes. A valid current account session is required to browse its history again.

The lifecycle adapter follows [Microsoft's MAUI lifecycle mapping](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0): Deactivated represents loss of focus, while Stopped represents a window becoming invisible. This implementation responds to all-window Stopped/Destroying, not Deactivated. The mapping supports the design; actual device event/timer behavior still needs the acceptance checks below.

**TimeSpent is derived from the session ledger**, not an incrementing task field. It sums exact elapsed ticks for Focus sessions only, including completed, manually stopped, interrupted, and currently tracked focus time. Paused gaps and all breaks are excluded. Display rounds to whole seconds only after summing, so fractional sessions are not lost one by one. Active display can be a few seconds ahead of its durable checkpoint.

Task totals include all recurrence occurrences and remain after task deletion/completion/rename; session snapshots identify the task/occurrence and capture its title at start. Totals use each task's latest captured title plus a short ID to distinguish duplicates. Current pending task rows show their local TimeSpent. The history list shows the latest 200 records, while totals include the entire scoped ledger. There is no manual time editing or destructive history clearing in this increment.

## Structure and persistence

| Component | Responsibility |
| --- | --- |
| Domain/Focus | Preferences, session lifecycle/reasons, duration rules, monotonic delta checks, pure TimeSpent aggregation |
| SqliteFocusStore | Independent focus.db3 schema 1, transactional starts/checkpoints/preferences, revisions, one-active partial unique index, corruption/newer-version safety |
| FocusTaskCatalog | Reads only pending device tasks or the current non-expired account's cached tasks; never makes API requests |
| FocusService | Shared coordinator, app-process lease, serialized commands/heartbeats, lifecycle recovery, source/account checks, checkpoint failure recovery |
| FocusRuntime | Process-wide one-second heartbeat and all-window stop/resume integration |
| FocusViewModel/page | Scoped selection, countdown/control, saved durations, task totals, suggestions, recent history, session-change clearing |

FocusSessions stores a stable GUID, scope, monotonic sequence, revision, active marker, and validated immutable session JSON. It includes kind, target/elapsed ticks, lifecycle state/reason, UTC times, task ID/title/recurrence snapshot. FocusPreferences stores scoped validated duration JSON. A partial unique index permits only one active record (Running or Paused). Checkpoints update the same row; duplicate stop/completion does not add another row or credit time twice. Revision/identity checks reject stale writes, decreased elapsed time, paused-time increments, and rewritten task snapshots.

`focus.db3.runtime.lock` uses an OS-held exclusive file handle. One app process can write/recover the runtime; another instance may read its authorized history but cannot start/control sessions until the owner closes and it acquires the lease. Closing/crashing releases the handle; the empty companion file can remain. Other windows in the same process share the singleton coordinator. File exclusivity, SQLite transactions/indexes, and revisions provide separate guards.

Task, sync, views, and reminders remain at versions **6 / 6 / 3 / 1**. PostgreSQL remains at eight migrations, sync protocol 6. No existing task/source file is upgraded by this feature beyond its already-supported initializer. focus.db3 initializes its new version 1 when focus runtime/history first opens. Unsupported newer versions, corrupt JSON, mismatched row/JSON identity, and stale writes fail without reset. Focus checkpoints/preferences share transactions; failed writes cannot partially credit time or save a losing start's settings.

The ledger contains ordinary **unencrypted local task metadata**, like existing caches. It contains no bearer tokens/passwords. Local history is intentionally retained independently of tasks; release work still needs export, retention, data-protection/recovery policies, and large-history performance checks. The current small-dataset store materializes a scope's history to derive totals, while the UI limits recent rows. No scale/performance claim is made.

## Failure and recovery behavior

A failed checkpoint halts accrual. Refresh attempts to reread and terminate the active record at the last durable checkpoint; it does not retry speculative duration or keep billing while storage is unavailable. The page reports that tracking was interrupted. If storage/schema data is still unreadable, the existing file remains; no task data, focus rows, or settings are silently deleted. Start a new session after reviewing recovered history.

Commands capture the current scope/session context and expected active session ID. An old window's Stop cannot stop a newer session. Account changes during task lookup cannot create a session for the old account. Reads/polls guard scope changes and clear private rows. SQL scopes and embedded JSON identities are checked together. The runtime never looks up task titles in another account to present a busy message.

## Run and verify

No new API database update or package installation is needed for this increment. Rebuild/run the client using:

```powershell
.\scripts\Run-Windows.ps1
```

For existing API accounts, retain the configured API, its eight migrations, and trusted HTTPS setup from RECURRENCE.md/DATABASE.md. Do not delete task/cache/history files to upgrade.

Automated verification uses exact portable client service/ViewModel source, real temporary SQLite files, controlled wall/monotonic clocks, forced write failures, concurrent starts, stale writes, process leases, account/origin isolation, and history aggregation. Existing API/PostgreSQL tests still run in the full suite; they do not turn local focus history into server data. Windows compilation validates XAML/lifecycle API references but does not prove native timer/lifecycle behavior.

## Hands-on acceptance (pending)

1. Start a short focus block, navigate away, work in another desktop app, return, pause/resume, and stop early. Check countdown, fractional/total time, one shared record, and unchanged task completion/recurrence.
2. Finish focus/short/long break sessions. Verify completed focus suggestions, manual break start, no break TimeSpent, no automatic next session, and the absence of any promised background alarm.
3. Close/restart or force-close during running/paused sessions. Confirm recovery retains only a saved checkpoint, marks Interrupted, never credits the closed interval, and does not auto-resume. Check sleep/lock/minimize/all-window stop/resume behavior and a long UI/runtime stall.
4. Use two windows, then two app processes. Confirm one active session/lease owner, readable authorized history, stale control rejection, and safe takeover after the owner exits. Check history scrolling while the countdown polls.
5. Complete/delete/rename a task or change/advance its recurrence in another window/replica; verify task/occurrence interruption and snapshot history. Confirm other field edits keep tracking and old task totals remain.
6. Switch device/account/API, log out, expire the session, and change account while starting. Confirm private rows/drafts clear and old history remains isolated; device focus is independent.
7. Check saved duration validation, settings reload versus unsaved drafts, scope totals, duplicate titles, archived tasks, UTC labels, light/dark/narrow layouts, keyboard/screen readers, and mobile compilation/device lifecycle separately.

## Learning focus

Separate elapsed duration from timestamps, live display from durable checkpoints, and a session ledger from mutable totals. Derive TimeSpent once from stable session IDs instead of adding time again after retries. Combine a process lease, one-active SQL index, revision checks, and scope guards rather than relying on a timer callback alone. Keep task completion, recurrence, breaks, and local history as distinct actions.
