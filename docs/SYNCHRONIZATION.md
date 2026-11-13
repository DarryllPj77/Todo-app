# Offline synchronization (Phase 10)

## Scope and storage boundaries

The task-list toolbar now has **Synced tasks**, alongside the Phase 9 **Account & server** page. Sign in through Account first, then open Synced tasks. Device-only tasks in `todoapp.db3` remain separate and are never silently assigned, copied, or uploaded. Account & server remains a direct online workspace for testing the REST API. Synced tasks reads/writes its own account cache even when requests fail.

`sync.db3` uses its own schema version 5 (upgraded without rewriting legacy requests on read) and a `ReplicaPartitions` table. A partition key combines the canonical HTTPS API origin and authenticated account ID. One partition state contains cached rows, the download cursor, immutable pending operations, latest local intent, conflicts, and retry state. A single SQLite transaction updates them together. This JSON state per partition is a deliberate small-dataset implementation; it avoids adding a second ORM. Larger datasets should use normalized replica/outbox tables and narrower queries.

Session tokens remain in SecureStorage. Task caches are ordinary SQLite files, **not encrypted**. Sign-out clears the visible account workspace but retains queued edits for reauthentication to the same origin/account. Different accounts/origins never load or send that partition. Cache hiding is an application boundary, not protection from someone with filesystem access to the same OS profile. There is no cache-purge UI in this phase; do not delete the cache to fix an error while it contains unacknowledged edits.

A valid saved session can select its cached account before online verification, allowing cached work after an offline restart. Network sync requires verification and an unexpired token. The existing session rules reject expired saved tokens on restart and sign out when expiry is detected during a protected request; cached work remains on disk, but selecting it again requires signing into the same account. There is no offline login or refresh-token flow.

## Server protocol

All routes require the usual JWT/account validation. Ownership always comes from the token.

| Route | Contract |
| --- | --- |
| `GET /api/sync?cursor=0` | Latest changed entries, ordered by increasing revision; at most 100 per page, with Cursor and HasMore |
| `POST /api/sync` | One immutable operation: OperationId, TaskId, BaseRevision, Deleted, and Task editable fields |

`SyncEntry` contains TaskId, Revision, Deleted, and Task (null for a deletion). Task contains title, description, date, priority, category name, completion, and optional tags. The API retains category reuse/validation rules; category management is still outside scope. Priority remains named JSON, unknown properties are rejected, IDs must be nonempty, revisions nonnegative, and deletion must have null Task. Sync creates preserve the client-generated task ID; ordinary REST creates still generate IDs on the server.

`SyncRecords` stores the latest revision/payload or deletion tombstone per account/task. `SyncReceipts` stores each account/operation's request and exact response. Both tables belong to Users. Receipts/tombstones are retained indefinitely in this learning implementation; pruning them would break retries/offline clients without a reviewed retention/reset protocol.

A writer obtains a per-account PostgreSQL transaction advisory lock **before** loading or changing tasks. Ordinary REST writes use the same lock and journal. Task changes, revision assignment, and receipts commit together. Revisions are allocated from the account's latest committed state, not wall-clock time or a sequence that could commit out of order. Advisory transaction locks release at transaction end ([PostgreSQL documentation](https://www.postgresql.org/docs/18/explicit-locking.html#ADVISORY-LOCKS)). This serializes writes within one account and is suitable for the MVP dataset; unrelated accounts can proceed independently.

The first locked operation lazily records existing owned tasks missing from the journal. It never assigns anonymous legacy tasks. Direct database edits bypassing the application are unsupported for already-journaled tasks; administrative imports/ownership changes require a reviewed journal update, not ad hoc SQL.

Apply follows this order:

1. Look up the account/operation receipt. Return its stored result for an identical retry; reused operation IDs with a different payload return 409.
2. Compare BaseRevision with the current task revision (zero for a previously unknown ID). A mismatch returns HTTP 200 with Conflict=true and the current server entry; it performs no task write.
3. Apply an accepted create/update/delete, record its new revision/tombstone, and save the receipt in the same transaction. A global task-ID collision belonging elsewhere returns a generic 409 without exposing its data.

Downloads expose the latest state per task, not an immutable event history. A later edit moves that task to a higher revision, so a client will receive it in a later page/pass. Each page is read under the same account lock. The client advances its cursor only in the transaction applying that page. Negative cursors return 400; a cursor beyond current server history returns 409 and leaves the cache intact. Database restore, journal loss, identity changes, or deliberate cursor reset require manual reconciliation; an epoch/reset/export protocol remains release-hardening work. Never reset a cursor by deleting pending work.

## Client algorithm and conflicts

Saving a task changes its cached local intent and creates an outgoing operation in the same transaction. The pending operation's ID/payload/base revision never change: its reply may have been lost after the server committed it. Further edits update local intent while that original operation remains pending. On acknowledgement, the client either adopts the canonical server result or creates the next operation for the newer local intent.

A receipt replay can be older than an entry already downloaded while waiting for retry. Acknowledgement never regresses that newer server revision. If additional local edits overlap that newer server entry, the client preserves them as a conflict instead of silently rebasing and overwriting it.

On conflict, both saved local intent and server state remain available. **Keep mine** creates a new operation against the displayed server revision (including explicit resurrection after a server deletion). **Use server** discards the saved local change and adopts the server entry, including deletion. Resolution also checks the displayed revision against the current cache, so a change downloaded in another window requires reloading/reviewing before either choice. Another concurrent server change can cause a new conflict. Device timestamps never decide the winner.

Sync sends up to 100 queued operations, then downloads up to 100 pages per pass. A singleton gate prevents overlapping engines across app windows. Captured origin/account/session-version checks reject stale network responses. An account switch can leave an old operation pending even if the server committed it; its receipt makes retry safe on the original account.

Failures retain operations and persist exponential retry delays of 4, 8, 16 seconds up to 5 minutes. 400/403/409 upload failures require explicit **Retry now** after review; other queued uploads observe their retry timestamps. 401/expiry uses normal sign-out without deleting the cache. **Retry now** overrides the delay using the same operation ID; it does not blindly create another task.

Sync runs on demand, on opening the page, on reported Internet connectivity changes, and every 30 seconds while the page is visible. The timer stops when leaving it. OS connectivity is a hint and cannot prove API reachability ([MAUI documentation](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/communication/networking?view=net-maui-10.0)); explicit Sync still works for a reachable LAN endpoint even if the OS reports otherwise. There is no always-running background service or closed-app sync.

Automatic sync does not clear unsubmitted editor drafts. If the cached row changes while a draft is open, saving is rejected with instructions to copy/reload/review the draft. Explicit reload/new/selection can replace unsubmitted text; save locally to retain work before leaving the page. Queued changes themselves survive restart.

## Run and manually verify

1. Stop your development API, apply all seven migrations using DATABASE.md, then restart its HTTPS profile. Trust the matching certificate on each test device; certificate validation remains enabled.
2. Close the older MAUI app before using `scripts/Run-Windows.ps1` to rebuild/run the new version. The Phase 10 verification build used isolated output so the running older app was not stopped or overwritten.
3. Sign in through Account & server and open Synced tasks. Sync once to download existing account tasks. Save a new task locally; verify queued count and then Sync acknowledgement.
4. Stop the API, create/edit/complete/delete disposable cached tasks, and confirm errors retain the queue. Restart the client while its saved session is valid and confirm cached data/operations remain. Restart the API and use Retry now; verify a single resulting server task per create.
5. On a second device using the same origin/account, Sync the same task, then edit it offline on both devices. Sync device A then B. On B inspect both versions, test Keep mine and Use server separately, and sync both devices again to verify convergence.
6. Repeat with deletion versus editing; explicit Keep mine may recreate the task. Test canceled deletion confirmation.
7. Sign out while a sync is in flight, then sign into a different account or apply another origin. Old rows/drafts must disappear. Return to the original account and confirm its queued edits remain without exposing them to the other account.
8. Check expiry, invalid HTTPS, narrow/light/dark layouts, screen readers, native storage/restart, and mobile suspension/resumption. These remain manual acceptance gates.

Automated checks cover actual client services/ViewModels, temporary SQLite files, real JWT/API middleware/PostgreSQL, receipt replay, conflicts, transaction rollback, pagination, and two replica convergence. The request test host uses in-memory HTTP transport and fake platform token storage. Native TLS, SecureStorage, connectivity events/timer lifecycle, and multi-device UI acceptance are not proved by those tests.

## Reminder integration (Phase 11)

Reminder settings stay device-local and do not alter SyncTaskData, operation receipts, server revisions, or migrations. After a committed sync-store write, MAUI's Written callback reconciles reminders against the latest cached task state. Completion/deletion/due-date changes cancel alerts when the change reaches this device. A reminder failure cannot fail or roll back a committed upload acknowledgement/download; no remote push cancellation is claimed. See [NOTIFICATIONS.md](NOTIFICATIONS.md).

## Tags and protocol 2 (Phase 12B)

Phase 12B introduced protocol 2 for tags; dependencies introduced 4 and the current API advertises 5 for planning. The engine checks capability before feature-aware writes; tags require at least 2 and Parent requires 3; Prerequisites requires 4. Older endpoints cause a blocked retained operation until upgrade/Retry now. Empty sets clear labels, while omitted/null sets preserve server tags on update. Missing legacy tags stay omitted during serialization to preserve old receipt bytes. Immutable TagSet equality prevents requeue loops. Tags retain whole-task conflict resolution without automatic unions. See [TAGS.md](TAGS.md). Upgrade client/API versions together.

## Hierarchy and protocol 3 (Phase 12C.1)

Parent creates precede child links; known live server children must detach/delete before a parent deletion uploads. A bounded pass or concurrent server changes may require another Sync. Missing ancestors and conflicting concurrent moves still undergo server validation. A structural rejection is a receipted conflict with current server state and a safe reason; local fields remain. Task hierarchy offers confirmed Keep local fields with a valid parent, guarded by expected local snapshot/server revision, or Use server. The ordinary whole-task Keep mine may conflict again if placement remains invalid.

Explicit Parent.Id=null means root; absent Parent preserves placement. Null outer Parent is omitted from legacy request serialization, so both pre-tag and tagged receipts can replay unchanged. Hierarchy introduced sync schema 3; prerequisites now advance it to 4 without rewriting old pending payloads on read. See [HIERARCHY.md](HIERARCHY.md) and [DEPENDENCIES.md](DEPENDENCIES.md) for the separate models, migration commands, and native acceptance.

## Dependencies and protocol 4 (Phase 12C.2)

Prerequisite-aware operations and every deletion require protocol 4, because deletes need the server incoming-link policy despite having no task snapshot. The engine waits for queued prerequisite creates/completion before dependent links/completion, and for known server incoming links to be removed before deletion. A bounded pass may need another Sync; server validation still detects missing tasks, cycles, and blocked completion. Rejection becomes a receipted conflict with local fields retained. Dependencies offers confirmed whole-local-task repair with a new prerequisite set, expected-local/server-revision guards, or Use server. Account changes clear drafts and reject old confirmations.

sync.db3 uses schema 4; absent/null Prerequisites stays omitted from legacy serialized mutations to retain receipt bytes. New snapshots carry explicit empty sets. Ordinary editors preserve omitted links; [] clears them. Reopening does not cascade completed dependent state. See DEPENDENCIES.md for storage tradeoffs, full policy, coordinated migration, and native acceptance.

## Planning and protocol 5 (Phase 12D)

Explicit Quadrant requires protocol 5 before upload, including Unclassified. Earlier feature/deletion minimums stay unchanged. Null Quadrant is omitted from legacy serialized SyncTaskData so earlier receipt bytes replay unchanged. New snapshots carry explicit classification. Queued normal edits preserve omitted planning, immutable pending payloads remain frozen, and later changes chain after acknowledgement.

Planning follows whole-task conflict semantics. The planning page displays both complete versions and offers confirmed Keep local fields with the chosen quadrant or Use server. Expected-local/revision checks and session guards reject stale/wrong-account actions. sync.db3 now uses schema 5; saved views use 3, reminders 1. See EISENHOWER.md for the coordinated migration, manual rules, and native acceptance.
