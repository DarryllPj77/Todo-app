# Google cursors and opt-in change monitoring - Phase 13D.2

This increment adds complete-page Google Calendar synchronization cursors and optional read-only change checks while the monitoring page/window is active. It flags changes to existing app links for the Phase 13D.1 review workflow. It never automatically publishes, adopts, deletes, completes a task, retries a pending provider write, or credits focus time. No operating-system background job is installed.

## Try it

1. Configure/connect Google on Windows as described in GOOGLE_CALENDAR.md and publish an account-local block through GOOGLE_EVENT_LINKS.md. Existing provider/native acceptance remains required; no live Google account is used by automated tests.
2. Close older builds if they lock normal output and run `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Windows.ps1`. calendar.db3 upgrades transactionally to schema 4 without resetting blocks/links/reviews. Older clients refuse schema 4; no API/EF migration is needed.
3. Open **Tools > Google change monitoring**, load owned calendars and choose the calendar containing your app-linked events. **Scan selected calendar now** runs a full initial check and saves its cursor. A manual scan does not enable automatic checks.
4. Change an app-linked Google event and scan the saved monitor again. Later scans use the saved cursor. Refresh **Tools > Google event links** to see a **Google change detected** notice, then select the link and load a fresh Google/local review before choosing a resolution.
5. To opt in, choose **Enable page-open checks for calendar** and review its confirmation. Initial/reset scans read the selected calendar's collection, including unrelated events in transit. Unlinked payloads are discarded and never stored. Only known app links receive notices. Prefer a dedicated test/planning calendar for acceptance.
6. Enabled monitors are checked on a roughly one-minute page timer while this page/window is active and online. Each cycle handles at most five due monitors, oldest completed checks first. Leaving/deactivating/stopping the page/window cancels current work. Reopening the active page resumes previously enabled, due monitors; no browser consent is opened automatically.
7. **Disable selected monitor** is local-only and requires no Google credential. It preserves the latest cursor/notices/links even if another window advanced the cursor after selection. **Scan selected saved monitor now** overrides a saved retry delay but does not automatically re-enable a paused/off monitor.

The destination is bound to the Todo API-origin/account, stable Google subject, Desktop client and actual calendar ID. Changing Google identity does not reuse another binding's cursor. Owner-role and granted-scope checks are independent; a provider access error pauses checks rather than treating the calendar as empty.

## Cursor policy

The initial and incremental queries use the same versioned parameters: `maxResults=100`, `singleEvents=false`, `showDeleted=true`. Incremental queries add only the saved `syncToken`, and pagination adds a `pageToken`. They do not add incompatible date/search/private-property filters. The service accepts a cursor only from the final page; malformed/missing final cursors, duplicate event IDs, repeated/inconsistent page tokens or bounds failures reject the whole batch. See [Google events.list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list).

Bounds are 100 pages, 10,000 event records, 256 KiB per HTTP response, 15 seconds per HTTP request and two minutes per scan. A large calendar may exceed these bounds and must use a smaller dedicated collection. No incomplete snapshot is interpreted as provider deletion. Unlinked event payloads are not written into SQLite; only their transient IDs are held for duplicate/completeness checking during the bounded scan.

An invalid cursor response (410) discards the in-memory partial batch and restarts one full scan without a token. Its new cursor is saved only after that full scan completes. If reset fails, the previous cursor remains for another attempt. This is a reset of provider scan state, never a wipe of local tasks, blocks, event links, pending operations or protected grants. See [Google incremental synchronization](https://developers.google.com/workspace/calendar/api/guides/sync).

## Atomic application and pending work

Before reading pages, the service captures the active link cohort for the exact binding, including pending rows. At commit, one SQLite transaction checks the unchanged monitor revision and the complete cohort's identities/revisions/membership. If a user reviews, publishes, resolves, detaches, closes or adds a link during network I/O, the batch does not advance its cursor. A later scan uses the prior cursor again. Cursor advancement and new notices commit together.

Pending create/update/delete rows are never modified or auto-retried by the feed. Their captured provider IDs/payloads and retry intent remain intact. A calendar containing only pending/no eligible links skips event collection reads; an automatic attempt defers that monitor for five minutes without counting a failure. A later explicit Retry still reconciles the pending operation through Phase 13C. Closed Deleted/Detached rows are not monitored.

For Linked/Conflict rows, managed resource fields/ETag are compared with the acknowledged baseline and any current review. A new change/missing/unsafe notice increments the link revision and clears an older review; the actual block, published baseline and pending state are not adopted or overwritten. Repeated equivalent notices do not repeatedly change the link. A review already showing the same Google version is retained. Loading/resolving a fresh review or staging/completing an explicit provider operation clears the old notice.

A full snapshot may flag an absent known event. An incremental result does not infer deletion from an omitted ID; only returned cancellation records indicate removal. Unsafe ownership/type/time changes produce a review notice rather than a forced write. Neither absence nor cancellation deletes a local block automatically. The existing explicit review re-fetches Google and rechecks local versions before any decision.

## Opt-in, retry and account handling

Monitoring is off by default. Enabling it persists an account/Google/client/calendar-bound flag after confirmation and owner validation. Calendar requests from monitoring are GET-only; existing OAuth refresh/identity verification can still use the Phase 13B token lifecycle. Up to 50 saved monitor profiles are allowed per Todo scope. Disable is authoritative for that binding and preserves the newest cursor; an in-flight scan cannot undo the opt-out.

Network/transient failures retain the previous cursor/notices and use persisted exponential retry delay (30 seconds up to 15 minutes). Provider Retry-After is honored up to a bounded day when longer. Authorization/access/configuration/inconsistent-page failures pause automatic checks. Reconnect/fix access, manually inspect/scan, then explicitly enable again. Manual Scan now overrides delay; it never creates a new provider event or changes the enabled flag. Cancellation/account-switch/stale-revision rejection does not overwrite a newer window's settings.

Todo logout/switch/expiry clears private screen state and cancels work. Google subject/client binding is checked before use. Existing grant blocking/revocation policy remains in GOOGLE_CALENDAR.md. No Google login, permission prompt, API address change or background job is triggered automatically. A local transaction already accepted for an authorized scope may finish during cancellation, but no partial page batch is accepted and no data is written under the new account's scope.

## Storage and privacy boundaries

calendar.db3 schema 4 adds GoogleCalendarFeeds keyed by Scope/Subject/ClientId/CalendarId with a positive local Revision and validated JSON. It stores query version 1, opt-in, opaque cursor, last completed scan, failures/retry time and safe status. Optional GoogleEventLink.Notice stores a change kind, observed ETag/time and safe message for a known link. Cursor values are not shown in UI or ToString output; they are scan metadata, not authorization tokens. OAuth credentials remain in SecureStorage.

No complete remote calendar mirror, unlinked event description/title, attendee list or provider response body is cached. Local account/cache protection, cursor retention, backup/restore and release recovery still require acceptance. Other local file versions, server migrations and task-sync protocol are unchanged.

## Native/provider acceptance still required

- [ ] Real configured Google test calendar: full/paged/incremental checks, provider cancellation, expired token/410 reset and unrelated-event handling.
- [ ] Interrupt on later pages/reset, kill/restart, change membership/review/monitor opt-in in another window; no partial cursor or lost pending intent.
- [ ] Actual provider 401/403/404/410/429/503 and Retry-After, refresh/revocation, scope/client/Google-account changes, network/storage failures and re-enable policy.
- [ ] Windows activate/deactivate/minimize/back/navigation, page timer cancellation, offline/reconnect, two windows and native opt-in/disable confirmations.
- [ ] Selection/scrolling, narrow/large-text/light/dark/keyboard/screen-reader layout; supported mobile OAuth/toolchain work remains open.
- [ ] Earlier Phase 13A/B/C/D.1, secure storage/cache, retention, backup/restore and release gates. No unattended provider mutation/adoption is enabled by this increment.

## Verification recorded on 2026-11-14

All 529 tests passed, 0 skipped (470 portable and 59 PostgreSQL facts). The 24 new feed/monitor cases bring targeted calendar/OAuth/reconciliation/feed checks to 119 passing cases. The final isolated Windows solution/XAML build passed with 0 warnings/errors. Tests use fake Google HTTP and protected storage; no live Google account/calendar was contacted. Native/provider/mobile acceptance remains open.

Coverage includes schema-3 preservation, final-page atomicity, escaped stable incremental queries, no false delta absence, failed later page, duplicate/malformed paging, 410 reset/partial discard/failure, full absence/cancellation notices, stale versus equivalent reviews, unsafe notices and unchanged pending rows, cohort/membership/opt-out races, opt-in/backoff/Retry-After/manual override, auth pause and Google-free disable, cancellation/logout/confirmation, corrupt metadata, bounded scans, pending-only deferral and stale-selection opt-out preserving newer cursor progress. PostgreSQL test databases were cleaned up; the repository cluster was stopped and the installed service remains running.
