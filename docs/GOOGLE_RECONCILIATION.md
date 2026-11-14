# Reviewed Google reconciliation - Phase 13D.1

The Google event-links screen now supports explicit review of a linked/conflicted event and a confirmed choice between local and Google fields. This is manual reconciliation of known links, not a background sync loop. Phase 13D.2 now adds separate cursor/reset handling and opt-in read-only monitoring; see [GOOGLE_MONITORING.md](GOOGLE_MONITORING.md). Automatic provider mutation/adoption remains disabled. Earlier Google/native/mobile acceptance remains open.

## Try it

1. Follow GOOGLE_CALENDAR.md and GOOGLE_EVENT_LINKS.md to connect Google on Windows and publish a saved SyncedAccount time block into an owned test calendar.
2. Rebuild using `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Windows.ps1` after closing older builds that lock normal output. calendar.db3 now upgrades transactionally to schema 4 (schema 3 introduced reviews), preserving blocks/links. Older clients refuse the newer version; no server migration is needed.
3. Change the linked event's title/time in Google Calendar. In **Tools > Google event links**, refresh local links, select the Linked/Conflict row and choose **Load Google / local review**.
4. Inspect the displayed local block and Google snapshot, including their UTC ranges and review timestamp. Loading a review saves comparison metadata but does not change either calendar version. Pending operations must first be reconciled through the existing confirmed Retry action.
5. Choose one of the following explicit resolutions. Each confirmation includes the saved review; provider/local versions are checked again before applying it.

| Choice | Behavior |
| --- | --- |
| **Use reviewed Google version** | Updates the existing local block's title/start/end and acknowledges the provider ETag. The block's ID, task ID, scope and captured zone remain. If the local block is missing, only the link is acknowledged; a block is never recreated. |
| **Keep reviewed local version and push** | Stages a durable update from the reviewed local block and conditionally pushes it against the reviewed Google ETag. The existing retry mechanism handles an uncertain reply. |
| **Accept reviewed Google deletion** | Rechecks absence, closes the link as Deleted, and retains the local block/task. Nothing is republished automatically. A later separately confirmed publish reserves a new provider ID. |
| **Detach local link only** | Stops managing the link locally without contacting Google. The old event may remain; another confirmed publish may create another event. This action works without a usable Google grant but still requires the original verified Todo account. |

Source task title/completion/due date/recurrence, task synchronization, reminders and focus history are not changed by these calendar choices. The ordinary task editor remains their write boundary. An adopted Google title becomes the block's new snapshot title; it does not rename the source task.

## Review and field ownership policy

The application owns only the managed event's title and fixed UTC start/end for this reconciliation workflow. Provider description, attendees, recurrence, event type and conference data are not adopted. The provider must still have the reserved event identity and private app ownership markers before live fields can be used. A foreign marker, recurring/instance/all-day/special event, attendee/conference conversion or unrepresentable response is recorded as **Unsafe**; its provider version cannot be overwritten, adopted or deleted through these review choices. Local detach remains available.

Google title/time edits do not apply automatically. Local edits likewise wait for explicit push. A single review stores an immutable ID, observation timestamp, full local block snapshot (or absence), provider ETag/title/UTC fields (or Missing/Unsafe), and a safe diagnostic. Loading a newer review increments the link revision and invalidates older windows' choices. The old acknowledged baseline is retained until a resolution succeeds.

Before a provider-backed resolution, the service re-verifies the Todo scope/session and original Google subject/client, checks the current journal revision, checks owner access, and GETs the same event again. Its ETag/fields or absence must equal the displayed review. The local transaction also checks the exact reviewed block, including task identity, zone and revision. A changed/deleted local block or changed/reappearing Google event rejects the stale choice instead of choosing a side silently. Google conditional modifications return 412 if the provider changes before a write; see [Google resource versions](https://developers.google.com/workspace/calendar/api/guides/version-resources).

A remote GET and a local SQLite commit cannot form one cross-system transaction. Adoption/accepted absence represents the snapshot just reviewed and rechecked; Google can change again later, requiring another review. A local decision already committed for an authorized scope may complete during cancellation/account switching; it is never applied to the new account's scope. An uncertain Google push remains a durable pending operation for explicit retry.

## Local adoption rules

- A local block must have a nonblank, trimmed 1-200 character title without control characters. Start/duration use whole minutes; elapsed duration is 1-1440 minutes. The captured local start date must remain in 2000-2099. Invalid Google fields can be corrected in Google and reviewed again, or replaced through an explicit valid local push.
- Original block ID, source task ID and time-zone ID remain unchanged. The provider's exact UTC instants are authoritative. Wall time is derived for display in the original zone, not guessed from the current device zone. An unavailable captured zone prevents adoption without resetting data.
- A provider UTC instant can unambiguously identify a repeated local DST time. Adoption preserves that instant even if the displayed wall clock is repeated; later manual rescheduling still uses the existing unambiguous-start policy. UTC export preserves the exact instant. Year bounds for an existing block follow its original local zone rather than the UTC year.
- Adoption checks overlap against the same scope inside the write transaction. If the provider range overlaps another saved local block, both the block and link remain unchanged. Adjacent blocks still use the existing half-open interval policy.
- The local block and acknowledged link snapshot/ETag are committed together. An orphan link can acknowledge a valid Google snapshot without restoring a deleted local block; remote cleanup still requires a separate explicit delete.
- Keep-local stages the reviewed local snapshot/ETag before HTTP, clears the old review and uses Phase 13C's conditional write/receipt recovery. New local edits do not rewrite an already pending operation. A 412 returns the link to Conflict for a fresh review.

## Detach and deletion semantics

Detach is distinct from confirmed provider deletion and from accepting reviewed provider absence. It sets a durable **Detached** state and retains identity metadata until a new explicit publish replaces that closed link. It never calls Google and can be used after revoked credentials, lost calendar access or unsupported event changes. Only Linked/Conflict rows can be detached; a pending uncertain operation must first be retried/reviewed to avoid silently abandoning a possible write.

The confirmation states that the remote event may remain and a later publish may create another event. A fresh publish after Deleted/Detached allocates a new provider identity and requires its own confirmation. No automatic abandon/recreate, deletion propagation, task completion or focus credit is inferred. Journal history/retention and backup/restore reconciliation still require release work.

## Storage and remaining automation

calendar.db3 schema 3 adds optional review JSON and Detached-state support. The 2-to-3 upgrade is additive; existing pending/linked snapshots remain intact. Staging a publish/delete or completing a pending operation clears its stale review. Invalid/newer data fails without reset. OAuth credentials remain in SecureStorage, not review JSON. Other local files, server migrations and task-sync protocol versions are unchanged.

The reviewed resolution path still GETs a specifically selected known event. Phase 13D.2 adds a separate cursor-based collection checker that flags existing links only; see [GOOGLE_MONITORING.md](GOOGLE_MONITORING.md). It advances cursors after complete pages and handles invalid cursors without losing local pending intent. There is no unlinked import, push subscription or unattended mutation/adoption.

## Native/provider acceptance still required

- [ ] Real Google test calendar: title/time divergence, review, each confirmed resolution, orphan link and remote disappearance/reappearance.
- [ ] Edit Google or the local block after loading review and during confirmation; stale actions must be rejected. Test another window replacing the review.
- [ ] Concurrent provider edit before conditional push, lost push reply, restart/retry and account logout/expiry; no unseen overwrite or duplicate identity.
- [ ] Local overlap rejection and rollback, unavailable zone, DST repeated instants, date bounds and invalid provider title/precision/duration.
- [ ] Unsafe/attendee/recurring/all-day event conversion, lost credentials/calendar access, local detach and explicit republish warning.
- [ ] Windows native scrolling/selection, keyboard, screen reader, narrow/large-text/light/dark layouts. Supported mobile OAuth/toolchain acceptance remains open.
- [ ] Complete earlier Phase 13A/B/C, protected storage, backup/restore, retention and release gates before enabling automation.

No live Google account has been operated by the automated test workflow; provider HTTP is mocked.

## Verification recorded on 2026-11-14

All 505 tests passed, 0 skipped (446 portable cases and 59 PostgreSQL facts). Twenty-five new review cases bring the targeted calendar/OAuth/reconciliation set to 95 passing checks. The final isolated Windows solution/XAML build passed with 0 warnings/errors. Tests use mock Google HTTP and protected storage; no live Google account was contacted. Native/provider/mobile acceptance remains open.

Coverage includes schema-2 preservation, read-only/persisted side-by-side review, exact block-only adoption, overlap rollback, invalid title/precision/duration rejection plus valid local push, stale provider/local/replaced reviews, reviewed ETag/412/lost-reply recovery, missing/reappearing events, unsafe local detach without Google credentials, orphan acknowledgement/no resurrection, repeated DST instants, original-zone year bounds, confirmation/logout and pending-operation gates. PostgreSQL test databases were cleaned up and the repository cluster stopped; the installed service remains running.
