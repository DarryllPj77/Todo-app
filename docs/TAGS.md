# Task tags (Phase 12B)

## Scope and use

Tasks can now have multiple tags independent of their single optional category. Enter comma-separated labels such as `work, urgent` in the device editor, Account & server editor, or Synced tasks editor. Clearing the field and saving explicitly clears all tags. Categories keep their existing assignment/reuse behavior; tags do not rename categories.

A TagSet is an immutable per-task value, not a global Tag entity. There is no standalone tag browser, global rename/delete, tag color, or tag ownership ID to submit. Account ownership comes from the task's validated user. Identical label text on different tasks/accounts does not link their data.

Normalization trims labels, applies Unicode NFC, lowercases invariantly, removes duplicates, and sorts ordinally for stable output. Up to ten submitted nonempty labels are accepted, each 1-32 .NET characters after normalization, with no comma/control characters. Commas delimit labels in the editor; blank CSV segments are ignored. A JSON array containing a blank/null/non-string label is rejected instead of treated as a valid tag. The normalized set has value equality and is immutable even if the caller changes the original input array.

Tag summaries appear in device, API, synced, and saved-view rows. The sync conflict panel shows local/server tag sets so the existing whole-task Keep mine / Use server choice can be reviewed. Tags use the current whole-task revision/conflict policy; concurrent sets are not silently unioned.

## Saved views

Search now also matches literal substrings in tag labels. The separate Tag rule supports:

| Rule | Behavior |
| --- | --- |
| Any | Do not restrict by tags |
| AllListed | Task contains every listed normalized tag |
| AnyListed | Task contains at least one listed normalized tag |
| Untagged | Task has no labels |

AllListed/AnyListed need at least one label. Exact tag rules do not confuse Work with Workshop; literal search can match substrings. Tag rules combine with the existing search/completion/priority/category/date rules using AND and persist in named views. Device/API/account scope isolation remains unchanged. No filter operation writes task or sync state.

## API contract and compatibility

Task responses include `tags` as a JSON string array. POST/PUT and sync Task data accept the same array via a shared TagSet JSON converter. Invalid arrays return 400 before task mutation. IDs, timestamps, category IDs, owner IDs, and unknown input fields remain protected by the existing DTO rules.

**Omitted or null tags preserve existing tags on updates. An explicit `[]` clears them.** Creates with omitted/null tags start empty. This is a deliberate backward-compatible exception to the other PUT fields' existing full-replacement/defaulting behavior. Include the other editable fields you want to retain, as before. New editors send an explicit set; toggling completion retains the current set. Old local TaskDetails callers that omit Tags also preserve them on update.

SyncPage now advertises `ProtocolVersion=2`. Before sending a tag-aware operation, SyncEngine probes the authenticated download endpoint and requires at least version 2. An older server causes a visible upgrade message and a retained, blocked outgoing operation; no tag-aware POST is attempted. After upgrading, **Retry now** retries the same operation ID. Legacy operations with Tags absent remain absent when serialized. This preserves their original server receipt request bytes, including operations queued before upgrading.

TagSet's deep equality matters for acknowledgement: a freshly deserialized set with the same labels equals the saved local intent, so it does not produce another upload indefinitely. A later offline change, including an explicit empty set, stays separate from the immutable in-flight operation and becomes its own next operation after acknowledgement. Old receipt responses may omit tags; the existing revision ordering prevents an older receipt from overriding a newer downloaded state.

Upgrade the server and clients together. Old sync binaries have strict JSON contracts and cannot be treated as compatible with new tagged snapshots; they may show an unreadable-response/newer-schema error. Do not run mixed old/new API deployments against tagged data. Preserving omitted fields and queued operation bytes is a data-migration guarantee, not a claim that every old binary understands tags.

## Storage and upgrade

TagSet is stored as a bounded JSON array in a text column on each task, rather than introducing a global tag catalog and join table. The current small-dataset query path filters materialized snapshots; database-level tag indexes/translated tag queries can be designed if actual data size warrants them.

PostgreSQL migration **20261113092317_AddTaskTags** adds required Tasks.Tags text with default `[]`. Existing task IDs, owners, categories, dates, completion, and timestamps are retained. EF uses explicit JSON conversion and immutable value comparison ([EF conversion guidance](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions), [value-comparer guidance](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers)). Domain remains independent of EF/MAUI. The JSON array shape is handled by an explicit converter ([System.Text.Json guidance](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/converters-how-to)). No package version was added or changed.

Device `todoapp.db3` upgrades atomically from schema 1 to **2** by adding required Tags text defaulting to `[]`. Unknown newer versions are rejected without reset. Task/tag writes share the existing transaction. `sync.db3` and `views.db3` advance to **schema 2** for the extended JSON formats; upgrades retain existing replica/outbox/definition payloads and their IDs/revisions instead of rewriting old requests. `reminders.db3` remains schema 1. Older apps reject these newer local versions; do not downgrade active client data.

Reminder behavior is unchanged: tag-only edits do not cancel an existing reminder; due-date/completion/deletion changes still do. Generic notification payloads contain no tag labels. Tag labels/filter metadata are ordinary unencrypted local task data, like existing caches, and are never bearer tokens.

For your configured Development environment:

1. Stop the development API and close older client builds. Keep existing databases/caches/outboxes; do not delete them to upgrade.
2. From the repository root, apply the four included server migrations using your existing secrets:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project src/TodoApp.Infrastructure --startup-project src/TodoApp.Api --context TodoDbContext
dotnet run --project src/TodoApp.Api --launch-profile https
```

3. Rebuild/run the new client with `scripts/Run-Windows.ps1`. Its local files upgrade when opened. Sign into the same API/account to retain its pending work and saved definitions.
4. Sync and inspect any existing conflicts. If an older API previously blocked a tag-aware upload, use Retry now after upgrading.

No automatic server migration or user-development database modification ran during implementation. Verification migrates only generated test databases. The migration's Down removes stored tags and is not a safe downgrade for active tagged data; review backup/roll-forward recovery first.

## Hands-on acceptance (pending)

1. Create a device task with `Work, urgent, work`, save, reopen, and confirm `urgent, work`. Edit/clear tags, complete/reopen, and verify its category/date/description remain correct.
2. Reject an overlong name, eleven labels, and invalid controls. Confirm the draft remains available and no partial task update occurs.
3. Use Account & server with the migrated API. Create/edit/clear tags, toggle completion, and test another account with the same labels without data leakage.
4. Use Synced tasks offline. Edit/clear tags before an older queued create has been acknowledged. Restart and sync; verify the original operation and later clear both finish without duplicate/endless uploads.
5. Sync two devices/accounts' replicas. Edit the same task's tags offline on two devices, inspect both sets on conflict, and try Keep mine/Use server separately.
6. Test an older API endpoint and confirm tag-aware upload is blocked with its queue retained. Upgrade, Retry now, and confirm convergence.
7. Save AllListed/AnyListed/Untagged views, restart, and reapply them with other rules. Check literal tag search versus exact matching, empty sets, normalized casing, and account/API switching.
8. Check schema-upgrade persistence, tag-only reminder retention, native editor scrolling/labels, light/dark/narrow layouts, and mobile targets. Actual native interaction/TLS/mobile compilation is not proved by portable tests or the Windows build.

Automated checks include immutable normalization/validation, SQLite upgrade/reopen, explicit clearing, stable legacy request bytes, pending-operation equality/chaining, saved-view filters, editor state, capability rejection, real PostgreSQL migration/ownership/validation, old receipt replay, and two tagged replicas converging through the real API/JWT middleware. The test host uses in-memory HTTP and fake platform token storage; native device acceptance remains separate.
