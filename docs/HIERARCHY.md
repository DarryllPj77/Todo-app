# Parent hierarchy and subtasks (Phase 12C.1)

## Use and rules

Open **Task hierarchy** from the device task list toolbar. Create tasks in the normal editors first, choose Device or SyncedAccount, refresh, select a task and its new parent, then Apply parent change. Choose Root task to detach. Device changes persist immediately; account changes queue in that origin/account's cache. Open Synced tasks to upload/download them. Device tasks are never automatically uploaded.

Each task has one optional parent and may have many children. Parents organize tasks. Completion, dates, category, tags, and reminders stay independent; completing a parent does not complete its children. Blocking dependencies now use a separate Phase 12C.2 model/screen; see DEPENDENCIES.md.

- Parent and child must exist in the same workspace/account. Missing, deleted, foreign, empty-ID, and self parents are rejected.
- Cycles and moves creating more than **16 levels including the root** are rejected. Validation includes the moved task's descendants.
- Move or delete live children before deleting a parent. There is no cascade or automatic detach.
- Parent-only moves retain the task's other fields and creation time. Effective placement changes update its edit time.
- Parent choices exclude self/descendants. Validation repeats at save time; stale cached windows must refresh.

The hierarchy list flattens a forest with deterministic title/ID ordering. Partial cache downloads may temporarily omit an ancestor; the screen marks missing/nonlive parents and avoids infinite loops when reading malformed cycles. No dependency edges or global parent catalog are created.

## API and compatibility

Task responses add nullable `parentTaskId`. Existing full task POST/PUT inputs accept optional `parent`:

```json
{"title":"Child","parent":{"id":"00000000-0000-0000-0000-000000000001"}}
```

Omitted/null outer `parent` preserves placement on updates; `{"parent":{"id":null}}` explicitly detaches. Creates with no parent start at root. Other editable fields retain their existing full-PUT semantics. A dedicated authorized parent-only operation avoids replacing unrelated fields:

```http
PUT /api/tasks/{taskId}/parent
Content-Type: application/json

{"id":null}
```

Success returns the full task (200); an unavailable child returns 404; invalid hierarchy returns safe 409 Problem Details; empty IDs/unknown fields return 400. Foreign and nonexistent parent failures use the same message. Ownership comes from the token. The server's existing per-owner transaction advisory lock serializes validation, write, and revision journal, so simultaneous opposite moves cannot both commit a cycle.

Hierarchy introduced protocol **3**; current Phase 12C.2 advertises **4** for prerequisites (DEPENDENCIES.md). Parent-aware mutations require version 3 before upload; tagged mutations still require at least 2. An older API retains the immutable pending operation and reports the required upgrade; Retry now reuses its ID after upgrading. Absent Parent is omitted during serialization, preserving both pre-tag and tagged legacy receipt request bytes. New snapshots include an explicit root wrapper to distinguish root intent from a legacy omission. Coordinated API/client upgrade is required; old binaries are not supported against new hierarchy payloads/files.

The upload loop waits for a newly created parent to reach the server before sending a child link, and for known server children to detach/delete before a parent deletion. A bounded pass or concurrent remote work can require another Sync. Missing remote children/parents can still cause a conflict; ordering does not bypass server validation.

Invalid structural uploads produce a receipted conflict with the current server snapshot and a reason. Local fields remain saved. In Task hierarchy, review both complete versions and select a valid parent; **Keep local fields & apply parent** requires confirmation and creates a fresh operation against the displayed revision. A changed local/server version rejects stale repair. **Use server** discards that task's local intent. The ordinary whole-task Keep mine choice remains available but may conflict again if its parent is still invalid.

## Storage and upgrade

| Store | Current version / change |
| --- | --- |
| PostgreSQL | Fifth migration AddTaskHierarchy adds nullable Tasks.ParentTaskId, self FK ON DELETE RESTRICT, index, and non-self check |
| todoapp.db3 | Current schema 4; the 2-to-3 hierarchy upgrade adds nullable ParentTaskId with the same FK/check/index, transactionally after the tag upgrade |
| sync.db3 | Current schema 4 adds prerequisites after schema-3 parent-aware JSON; old pending payloads are retained without rewriting on read |
| views.db3 | Schema 2, unchanged; saved views remain flat query results |
| reminders.db3 | Schema 1, unchanged; parent-only moves retain reminder time |

Existing tasks become roots; tags, ownership, fields, timestamps, queues, receipts, and saved views remain. The FK prevents dangling IDs/direct deletion; cycle/depth/owner consistency is enforced by application services. Administrative SQL must preserve those additional rules. Do not downgrade active data: migration Down removes all parent relationships.

1. Stop the development API and close older clients. Retain/back up existing files and databases; do not reset queues.
2. Use your configured Development connection and JWT secret, then apply all six migrations (including AddTaskDependencies; DEPENDENCIES.md):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
dotnet run --project src/TodoApp.Api --launch-profile https
```

3. Rebuild/run the client with `scripts/Run-Windows.ps1`. Local files upgrade on access. Sign into the same origin/account, sync, and review conflicts. Use Retry now after upgrading an API that previously blocked hierarchy uploads.

Implementation verification only migrates generated test databases. Your development database has not been migrated automatically. Existing TLS/SecureStorage/mobile acceptance gates remain in SYNCHRONIZATION.md and NOTIFICATIONS.md.

## Hands-on acceptance (pending)

1. Create root/child/grandchild device tasks. Move, detach, refresh, restart, and confirm placement/fields persist. Reject self/descendant moves, a seventeenth level, and parent deletion while children exist.
2. Complete/reopen a parent and verify child completion, tags, dates, and reminders remain independent. Delete/move children, then delete the parent.
3. Use two account replicas over trusted HTTPS. Queue a parent and child offline, restart, sync both, and verify relationships without duplicate/endless uploads. Queue child and parent deletion and sync again.
4. Move A under B on one replica and B under A on another. Sync, review the rejected local move, cancel/confirm a different-parent repair, and try Use server. Verify stale repair/account switching cannot write the wrong workspace.
5. Test missing parents during partial downloads, duplicate titles, maximum depth, long field text, light/dark/narrow layout, keyboard/screen reader behavior, and native restart. Compile/test mobile targets separately.
6. Upgrade a disposable version-2 task/cache file with existing tags and pending work. Verify no reset. Test an older API: hierarchy upload stays queued, then Retry now succeeds against the migrated API.

Portable tests and real PostgreSQL integration cover structural rules, storage upgrades, ownership, concurrent moves, receipted conflicts, repair, upload ordering, and replica convergence. Windows compilation validates XAML/platform references; native interaction and mobile delivery are still pending.
