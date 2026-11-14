# Explicit Google event links - Phase 13C

This increment publishes saved **SyncedAccount** time blocks to an explicitly selected owned Google calendar on Windows. It supports create, explicit push of later local changes, confirmed remote deletion, and explicit retry of an uncertain operation. It does not run in the background, pull provider edits into tasks/blocks, resolve divergent conflicts, or infer links from imported ICS copies. Phase 13D remains the reconciliation/two-way milestone.

## Try it

1. Follow GOOGLE_CALENDAR.md to configure a Google **Desktop app** OAuth client, enable Calendar API, sign into the Todo API and connect the intended Google account. No new scope is added: the existing calendar-list read and owned-event grant is used.
2. Close an older client if it locks normal build output and run `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Windows.ps1`. The local calendar file upgrades transactionally from version 1 to 2; no API/EF migration is needed. Older clients refuse the newer file, so do not run them against this data after upgrading.
3. In **Tools > Calendar & time blocks**, select **SyncedAccount**, refresh its pending cached tasks and save a time block. Device blocks remain device-only and cannot be published through this account flow.
4. Open **Tools > Google event links**. Refresh local blocks/links, then explicitly choose **Load owned Google calendars**. Select the actual destination calendar and local block. Use a separate Google test calendar for acceptance.
5. Choose **Publish / push selected local block** and review the title, fixed UTC range and calendar in the confirmation. Creation sends only the block's title and start/end plus app ownership metadata. A local link alone is not evidence of remote success: inspect whether its state is Linked, Pending or Conflict.
6. Reschedule the local block in the planning screen, refresh the event-links screen, select the same calendar/block and explicitly push it again. A linked block cannot silently move to another Google subject, client or calendar.
7. Select a linked record and choose **Delete selected linked Google event** to remove that one provider event after confirmation. The local block/task and independently imported calendar copies remain. Deleting a local block alone never deletes a Google event; its link remains available for explicit cleanup.
8. After a timeout, cancellation or error, refresh local links. Select its pending row and **Retry selected pending operation**. The confirmation shows the saved operation and captured time range. Retry reconciles that original snapshot, even if the local block has since changed. Push the newer local revision only after pending work finishes.

Actual Google calendar listing/writes, native confirmations and restart recovery still require manual acceptance. Automated tests use mock provider responses; no Google account has been modified during implementation.

## State and identity

```text
confirmed publish -> PendingCreate -> Linked
confirmed push    -> PendingUpdate -> Linked
confirmed delete  -> PendingDelete -> Deleted
uncertain reply   -> same Pending state -> explicit retry
unsafe/divergent provider version -> Conflict (frozen)
Deleted + new confirmed publish -> new event/link identity
```

Each record binds the Todo API-origin/account partition, local block ID, verified Google subject, Desktop client, actual calendar ID and a random link identity. It reserves a UUID-based Google event ID and separate ownership proof before the first provider write. The ID stays fixed across retries. A successful deletion followed by a new confirmed publish reserves a fresh ID, so a deleted provider ID is not recycled. ICS iCalendar UIDs are not Google event resource IDs and are never adopted as links.

Google supports caller-specified event IDs with a restricted lowercase base32hex alphabet and recommends UUID-based IDs to minimize collisions. Distributed provider behavior means the application cannot promise an absolute global collision guarantee; ownership markers and postcondition checks prevent adopting or overwriting an unrelated returned event. See [Google events.insert](https://developers.google.com/workspace/calendar/api/v3/reference/events/insert).

## Durable journal and concurrency

`calendar.db3` schema 2 adds GoogleEventLinks with `(Scope, BlockId)` primary key, monotonic local Revision and validated JSON. Version-1 blocks are preserved. A write transaction checks the current local block revision and expected link revision before recording a Pending operation, including the immutable desired title/time snapshot. No token is stored in this file; ownership proof is linkage metadata, not Google authorization. Access/refresh tokens remain in the Phase 13B vault.

Provider identity and pending operations survive local block deletion, cancellation, process restart and uncertain replies. A receipt acknowledges only the exact pending link revision/snapshot; stale windows cannot acknowledge or stage against an unseen operation. Concurrent attempts use the same reserved event ID and conditional event version, while local revision checks reject stale receipts. A completed write whose receipt was not observed remains Pending until reconciliation. Journal corruption/newer schema fails without reset; retention and backup/restore reconciliation remain release work.

## Provider operations

- Calendar listing is paginated and limited to owner-role entries. A repeated/excessive page token fails rather than returning partial choices. Calendar access is checked again through CalendarList before an operation; no `primary` alias or arbitrary manually entered destination is substituted. See [Google calendarList.list](https://developers.google.com/workspace/calendar/api/v3/reference/calendarList/list).
- Before retrying or sending a provider write, the app verifies the Todo scope/session, Google identity/client, current journal operation and owned destination. Requests use a separate fixed-origin HTTPS client with no redirects, 15-second timeout and 256-KiB response bound. Provider errors are summarized without displaying raw response bodies or tokens.
- Create first looks up the reserved ID. A matching app-owned event is acknowledged; otherwise it inserts with that same ID. An insert 409 is reconciled by reading that ID and verifying ownership/postcondition. No fresh ID is allocated to escape an uncertain create. Per Google's API, insertion does not support If-Match; reserved IDs provide the retry identity instead.
- Updates read the linked event, verify its private ownership markers, compare the saved ETag, then PATCH only summary/start/end with **If-Match**. Unspecified provider fields are preserved. A 412 becomes a Conflict rather than an automatic overwrite. A matching desired postcondition acknowledges a previously uncertain write without sending it again. See [conditional versions](https://developers.google.com/workspace/calendar/api/guides/version-resources) and [events.patch](https://developers.google.com/workspace/calendar/api/v3/reference/events/patch).
- Delete reads the event, verifies ownership and saved ETag, then uses conditional DELETE. A missing/gone/canceled event finishes a pending deletion without another mutation. A changed ETag blocks deletion. Lost delete replies are resolved by checking absence, not by deleting a different event. See [events.delete](https://developers.google.com/workspace/calendar/api/v3/reference/events/delete).
- New events are private, opaque, single non-recurring default events with default reminders disabled and no attendees/conference/description/task ID/account ID. Private `todoLink`/`todoProof` properties identify app ownership. Requests use `sendUpdates=none`; the app does not create invitations or calendar recurrence rules.
- A linked event with mismatched markers, attendees, recurrence/instance metadata, conference data, all-day times or a non-default event type is frozen for review. A remote deletion is not recreated by an update. Exact desired-field matches may reconcile an uncertain operation; divergent title/time data or a changed ETag before a write is not overwritten automatically. No task completion or focus time is inferred from a provider event.

## Conflicts, disconnect and scope changes

Conflicts retain local desired/published snapshots and the last acknowledged ETag. This increment deliberately offers no force-overwrite, auto-relink or destructive abandon-and-recreate action. Inspect the provider event separately and complete the Phase 13D conflict-review workflow before resolving a divergent link. A confirmed deletion is currently offered only for Linked state; pending deletion is continued only through confirmed Retry.

Todo logout/switch/expiry cancels work and clears the screen. A write already sent may still finish at Google; its original-scope pending record remains for later recovery. Reconnect the same Todo/Google identity/client and retry it. Changing Google identity/client does not retarget old links. Google disconnect/revocation removes credentials/permission as documented in GOOGLE_CALENDAR.md; it does not remove event links, provider events or local blocks. Mobile and Debug layout preview cannot send these provider requests.

## Native/provider acceptance still required

- [ ] Real configured Google test calendar: list/select, create, local reschedule/push, confirm conditional update/delete, verify title/UTC range in the calendar UI.
- [ ] Interrupt after remote mutation but before local receipt, restart, refresh/retry; confirm reserved identity and no duplicate creation in actual provider behavior.
- [ ] Provider edit/deletion, attendee/recurrence conversion, lost owner access, revoked token, 403/404/409/410/412/429 and network/storage failures; no unseen overwrite.
- [ ] Two windows/processes, stale local block/link selection, local block deletion, Google/Todo account switch, confirmation cancellation, logout and expiry during provider I/O.
- [ ] Native narrow/light/dark/keyboard/large-text/screen-reader layout, back/cancel handling and supported mobile OAuth/toolchain work.
- [ ] Earlier Phase 13A/13B and release acceptance, including protected credentials, cache retention, backup/restore and journal recovery.

No background synchronization, incremental sync cursor, provider-to-local field adoption or automatic conflict resolution is enabled by this increment.

## Verification recorded on 2026-11-14

All 480 tests passed, 0 skipped (421 portable and 59 PostgreSQL facts). The 23 new event-link cases plus existing calendar/OAuth cases total 70 passing targeted checks. The final isolated Windows solution/XAML build passed with 0 warnings/errors. Tests use fake Google HTTP/token storage; no live Google account or calendar was contacted. The repository PostgreSQL test cluster was cleaned/stopped; the installed PostgreSQL service remains running.

The new cases cover schema-1 preservation and invalid journal identity, reserved IDs/private markers, same-ID 409 reconciliation, lost create/PATCH/DELETE replies, retained snapshots after newer local edits, conditional request headers, divergent edits and ownership/attendee/recurrence/412 conflicts, orphan link cleanup, remote deletion/no recreation, fresh identity after deletion, stale block/link/CAS checks, destination/Google/Todo binding, owner filtering/pagination cycles, confirmation cancellation and logout after a provider mutation. Actual Google/native/mobile acceptance remains unchecked.
