# Calendar planning - Phase 13A

Google Calendar is the user's chosen first provider. This increment implements local time blocking and selected-block iCalendar export. Windows Google consent/token lifecycle is now implemented separately in Phase 13B; see [GOOGLE_CALENDAR.md](GOOGLE_CALENDAR.md). Explicit provider event links are now implemented separately in Phase 13C; see [GOOGLE_EVENT_LINKS.md](GOOGLE_EVENT_LINKS.md). **Automatic/two-way synchronization is not connected yet.** No Google credentials or API setup are required to try local planning/export.

## Blueprint and boundaries

```text
Device tasks / current account cache
              |
         CalendarViewModel
              |
         calendar.db3
              |
      one selected saved block
              |
     RFC 5545 .ics file -> system share -> user chooses destination/import
```

TimeBlock captures an ID, device/account-origin scope, source task ID/title, original local start/time-zone ID, fixed UTC start/end, local revision, and update timestamp. TimeBlockRules handles start/duration validation and half-open interval overlap. SqliteCalendarStore owns transactions and stale-window checks. CalendarViewModel handles drafts, selection, account guards and commands; CalendarFileExporter handles the native share request. There is no task mutation, outbox message, external event mapping or provider token in this increment.

## Try it

1. Close an older client if it locks the normal output and run `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Windows.ps1` from the repository root.
2. Add an incomplete device task. Open **Tools > Calendar & time blocks** and choose Device. For SyncedAccount, sign in through Account & server first and load tasks through Synced tasks.
3. Choose a pending task, start date/time and 1-1440 whole elapsed minutes. Click **Save local block**. Conflicted/deleted/completed cached tasks are not offered for new blocks.
4. Select a saved block to reschedule it. The original time zone/title are retained. Use **New block** to plan a different task. Refresh/New block asks before discarding a dirty draft. Finish or discard a draft before selecting another saved row. Workspace/account changes clear drafts immediately.
5. Select a saved block with no unsaved changes and click **Export selected saved block (.ics)**. Choose a destination through the system share UI. Export contains its captured task title and fixed UTC times; task descriptions, task/account IDs, API origins and tokens are omitted. A share request does not prove the recipient imported it.
6. For Google Calendar on a computer, use **Settings > Import & export**, choose the `.ics` file and the intended destination calendar, then Import. Google's instructions describe ICS imports as independent copies, not live synchronization. See [Google import help](https://support.google.com/calendar/answer/37118?hl=en).

Repeated imports may duplicate events. Stable UIDs and sequence numbers identify local revisions but do not guarantee an importer's update behavior. Rescheduling/deleting a local block does not change an imported event; remove or edit imported copies separately. Try imports in a separate test calendar first.

## Time and identity policy

- Starts use the device's local time zone for new blocks. Existing blocks retain their captured zone. Years 2000-2099 and whole-minute starts are accepted; daylight-saving skipped/repeated local starts are rejected. A duration always means elapsed UTC minutes, including when clocks change mid-block. Original-zone wall time is displayed even after travel.
- UTC instants remain authoritative after an OS time-zone rule update. Reading/exporting does not recalculate them. Explicit rescheduling uses the current rules for the captured zone; an unavailable zone fails without changing the block.
- Overlaps are rejected transactionally within one workspace. End is exclusive, so adjacent blocks are allowed. Device and different account/API-origin scopes can overlap. This is not a provider free/busy check.
- Multiple blocks can reference a task at different times. Titles are snapshots; task renames/completion/deletion and recurrence advancement do not retitle, cancel or repeat blocks. No task due date becomes an event automatically, and no RRULE/attendee/alarm/invitation is generated.
- One local GUID is the event UID for every export of a block. Revision minus one becomes SEQUENCE. UTC DTSTART/DTEND, deterministic ordering, text escaping, CRLF and UTF-8-aware 75-octet folding follow [RFC 5545](https://www.rfc-editor.org/rfc/rfc5545).

## Storage, concurrency and account handling

`calendar.db3` was introduced as an independent schema-1 local file. Phase 13C transactionally upgrades it to schema 2 with the event-link journal, preserving existing blocks. CalendarBlocks stores ID, scope, revision and validated JSON, with a scope index. Each operation opens its own nonpooled connection/transaction. Save checks the expected revision and overlap under one write transaction; stale updates/deletes fail rather than overwrite another window. Limit: 500 blocks per scope. Unreadable/newer/corrupt data is not reset. No task/sync/view/reminder/focus/server schema or protocol changes are needed.

Account blocks are scoped to the active Todo API origin/account and remain device-local. New account blocks read only live, pending, nonconflicted cache rows. Logout/account/workspace changes clear visible private rows/drafts. Account expiry is checked when loading or performing an action, including immediately before handing a prepared export to the share API. A write already started for an authorized scope may finish there after logout, but its result is not shown in another scope. This does not log into Google or bind a Todo account to a Google account.

Export files remain in the app's `CacheDirectory/calendar-exports` so the receiving app can read them after the share API returns. They are not automatically removed on logout or local block deletion; received/imported copies also remain. Cache retention/protection/export recovery remain release-hardening work. Android's FileProvider override exposes only that dedicated export directory, following [MAUI sharing guidance](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/data/share?view=net-maui-10.0); iPad sharing has a presentation anchor. Native sharing/import and mobile resource packaging still require acceptance.

## Remaining Phase 13 increments

- **13B Windows consent/token implementation is complete:** configurable Desktop client, browser PKCE/state, secure account-bound tokens, refresh/check, cancellation and explicit disconnect/revocation. Live Google/native acceptance and mobile authorization remain pending; see [GOOGLE_CALENDAR.md](GOOGLE_CALENDAR.md).
- **13C Windows event-link implementation is complete:** owned calendar selection, durable pending operations, captured block snapshots, conditional updates/deletes and uncertain-write recovery. Actual Google/native acceptance remains open; see [GOOGLE_EVENT_LINKS.md](GOOGLE_EVENT_LINKS.md). Imported ICS copies are not adopted as links.
- **13D - Reconciliation/two-way policy:** decide field ownership and review conflicts before enabling remote-to-local changes, incremental tokens/resets, remote deletion, recurring/provider event exceptions, timezone shifts and revoked access. A calendar event must never implicitly complete a task or credit focus history.

## Native acceptance still required

- [ ] Windows create/reopen/reschedule, keyboard and scrolling, discard/back navigation, two-window stale edits, narrow/large-text/light/dark layouts.
- [ ] Native share dialog cancel/failure/success; inspect saved ICS; manually import to a Google test calendar; verify title/time/timezone and imported-copy limitations.
- [ ] Account logout/switch/expiry during load, confirmation and file preparation; no old-account data shown or shared afterwards.
- [ ] Android/iOS/Mac Catalyst build, FileProvider URI/root restriction, iPad popover, phone date/time keyboards, lifecycle/restart and account data/cache protection.
- [ ] Complete earlier Phase 12 and release acceptance gates. Portable tests and Windows compilation are separate evidence.

## Verification recorded on 2026-11-14

The full suite passed 431 tests, 0 skipped (372 portable and 59 PostgreSQL facts). Twenty-one new calendar cases cover DST ambiguity/gaps and elapsed duration, invalid input, persistence/reopen, concurrent overlap, adjacency/scopes, stale updates/deletes/exports, corrupt/newer files, stable UID/sequence, UTF-8 folding/injection escaping, draft confirmation, retained snapshots, account expiry/logout confirmation races, and unchanged task sync mutations. After a final persisted-data validation change, all 21 calendar cases passed again. The final Windows solution/XAML build passed with 0 warnings/errors. Android FileProvider XML is well-formed but has not been compiled on the Android toolchain.

No native calendar screen/share/import or Google OAuth verification was performed. The implementation does not assert that sharing completed, that Google imported anything, or that repeated import updates an event. Full native acceptance remains open. All generated PostgreSQL test databases were cleaned up; the repository test cluster was stopped and the installed PostgreSQL service remains running.
