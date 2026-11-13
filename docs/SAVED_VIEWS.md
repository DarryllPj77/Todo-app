# Custom filters and saved views (Phase 12A + 12B tags)

## Scope

Phase 12 is deliberately split into reviewable increments. Phase 12A adds custom filters, search, sorting, and named views. Phase 12B adds tags and tag rules; hierarchy, dependencies, urgency/importance, recurrence, time tracking, and the full desktop three-pane layout remain later Phase 12 work. This increment does not claim those features or skip ahead to calendar integration.

Close the older app, rebuild with `scripts/Run-Windows.ps1`, and open **Saved views** from the main task-list toolbar. This page provides a read-only explorer plus navigation to the normal task editor/workspace. Existing task-list filters and sync/reminder screens stay available.

Choose **Device** or **SyncedAccount**. Device views use the unassigned device-only task store. Account views read only the currently selected API/account's cache; sign in through Account & server and use Synced tasks first to populate it. This page does not call the API or sync automatically. An empty cache does not mean the server has no tasks.

## Filtering and sorting

Change fields, then **Apply filters**. Typing or choosing a saved view changes the draft controls but does not silently change the applied results. **Reset to all tasks** clears the draft/applied rules and selected view. **Refresh saved views & current results** reloads saved definitions and recomputes the currently applied results; it does not implicitly apply an unfinished filter draft.

All selected rules combine with AND. Literal search matches a case-insensitive substring in the title, description, category, or tag labels (OR across those fields). It is not a regular expression, SQL predicate, or tokenized language. Search is trimmed and limited to 200 characters.

| Rule | Behavior |
| --- | --- |
| Completion | Any, Pending, Completed |
| Priority | Any, Low, Normal, High |
| Category | Any; Named with an exact case-insensitive trimmed name; or Uncategorized |
| Due | Any, Today, Overdue, NextSevenDays, Undated, Dated |
| Tags | Any, AllListed, AnyListed, Untagged; listed rules need at least one normalized tag |
| Sort | Title ascending; DueDate ascending with undated last; Priority descending (High first) |

A Named category requires 1-100 characters. Category matching is exact; Work does not include Workshop. A category need not already exist, so a rule can be saved before matching tasks exist.

Today is computed from the **device's current calendar day** whenever results load. NextSevenDays includes today through six days after today, not seven full 24-hour periods. Overdue means a date strictly before today. Undated tasks match neither Today/Overdue/NextSevenDays. Relative rules are saved as rules rather than fixed dates and are recomputed when applying/refreshing/reopening; the page has no midnight background refresh timer. Completed tasks remain eligible for due/priority rules unless Completion=Pending is explicitly chosen.

Sorting uses ordinal case-insensitive title ordering and a stable task-ID tie-breaker. DueDate and Priority orders then use the same title/ID tie-breakers. No newest-first sort is offered because the current sync cache does not retain a trustworthy creation timestamp.

Matching task rows show their current fields and device/sync state. Selecting a device row exposes **Edit selected device task**, using the normal editor and its reminder-aware save path. Selecting an account row exposes **Open Synced tasks workspace**; select/edit/resolve the task there. Account results exclude local deletion tombstones, but the status always reports total queued operations and conflicts across the whole account cache, including items outside the active filters. Filters neither resolve conflicts nor imply that a filtered-out task was deleted.

## Saving and managing definitions

Enter a 1-60 character name and use **Save as new view & apply** to create another definition. Names are unique within a workspace using trimmed, invariant-uppercase keys. Duplicate names produce a validation error instead of silently overwriting another view. There are at most 50 saved views per workspace; updating an existing view remains possible at the limit.

Select a saved view to load its rule draft/name, then Apply. Use **Update / rename selected view & apply** to explicitly change that definition. **Delete selected view (keep tasks)** asks for confirmation and removes only the definition. The currently applied rule can remain in use after deletion; no task, category, outbox item, reminder, or server record is deleted.

Each definition has an ID and local revision. Updates/deletes require the revision originally loaded by that window. If another window changed/deleted it, refresh and review instead of overwriting it silently. Unsaved filter text survives validation/conflict errors so it can be copied or saved under another name. Deleting requires another scope/session check after confirmation to reject an account change while the dialog was open.

Saved views do not sync between devices. Device-only definitions remain independent of server login. Each account scope combines the canonical HTTPS API origin and user ID. Sign-out, another account, or another API immediately clears account results, selected names, drafts, and loaded definitions; its stored definitions remain for the original scope. A generation/session check discards a result that finishes loading after its workspace/account changed. Device drafts are not reset merely because server login changes.

## Model, storage, and algorithm

Domain/Views defines the immutable TaskViewDefinition, candidate snapshot, saved definition, and pure TaskViewQueries. Normalize validates bounds/enums and canonicalizes unused category input. Apply uses LINQ predicates followed by deterministic ordering and returns a materialized result without mutating source tasks.

SqliteTaskViewStore initializes **views.db3 schema version 2**, independent of all existing task/sync/reminder schemas and server migrations. SavedTaskViews stores ID, scope, display/name key, revision, and JSON rule definition. SQL parameters protect names/search payloads; transactions serialize duplicate/quota/revision checks with writes. Corrupt/newer schema or definition data fails without reset. Opening/reading/applying a view does not emit a sync-store Written callback.

TaskViewsViewModel separates editable rules from the applied definition, loads device tasks or account cache snapshots, and projects matching rows without creating task copies. Saved definitions and results are per-window state over the shared local store. The navigation service provides the new page and definition-delete confirmation; code-behind handles appearing/workspace-selection refresh only. Phase 12A added no package or task/API fields. Phase 12B adds the tag contract/migration described in TAGS.md; view definitions themselves remain local metadata.

Names, search text, category rules, and tag rules are ordinary **unencrypted local metadata** in the app's OS-user profile, like the existing caches. They never contain authentication tokens by design. Application account partitioning is not filesystem encryption or a separate local user-authentication system. Cache selection retains Phase 10 session behavior; after session expiry/sign-out, account selection may require signing in again.

## Manual acceptance (pending)

1. Rebuild after closing the older app. Open Saved views, choose Device, and confirm the same device tasks appear as on the main list.
2. Combine literal search, Pending, High, Named category, and a due rule. Verify Apply is required and Reset restores all tasks. Test null description/category/date and a category with no matching tasks.
3. Save a named view, close/reopen the app, select it, and Apply. Update/rename it; confirm a duplicate name is rejected while keeping the draft. Delete it with Cancel and confirm that actual deletion leaves tasks/reminders intact.
4. Create dates yesterday/today/tomorrow/day-six/day-seven and undated tasks. Check date boundaries in the device time zone and refresh after midnight. Verify completed tasks appear unless Pending is chosen.
5. Check all three sorts, equal titles, multiple priorities, and undated-last ordering.
6. Edit a matching device task through this page and return; confirm its membership changes under the applied rule and normal reminder cancellation still works.
7. Sign in and sync account A, then choose SyncedAccount and create a view. Switch account/API/sign out, including during refresh/delete confirmation, and verify no old names/results/drafts remain. Return to A and confirm its stored definitions remain. Device views must remain separate.
8. Confirm a zero-match filter still shows global queue/conflict counts. Open Synced tasks to manage cached edits/conflicts; verify no network write occurs simply from applying a filter.
9. Open two windows on the same definition. Update in one, then try stale update/delete in the other; verify the conflict message and preserved draft.
10. Check scrolling, toolbar overflow, screen readers, light/dark themes, and narrow/mobile layouts. Native view interaction and mobile target builds remain pending; compilation and portable tests do not prove them.

Automated tests cover query combinations/date boundaries/sorting, SQLite reopen/uniqueness/quota/parameters/concurrency/corruption, and actual ViewModel scope/draft/navigation/race behavior. The full existing reminder/sync/JWT/PostgreSQL suite remains part of regression verification. See DEVELOPMENT_PROGRESS.md for measured results.

Phase 12B upgrades old definition JSON without resetting IDs/revisions, adds exact tag-set predicates and literal tag search, and leaves other rule semantics intact. See [TAGS.md](TAGS.md) for canonical labels, explicit empty sets, compatibility, and the server/local upgrade sequence.
