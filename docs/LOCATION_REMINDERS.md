# Location reminders: Phase 14A

Phase 14A manual behavior remains available. Phase 14B.1 adds optional page-active native sessions for packaged Windows; see [NATIVE_GEOFENCES.md](NATIVE_GEOFENCES.md) for packaging, OS registration privacy, lifetime and cleanup limits. Opening the page starts only metadata reconciliation, not location sampling. Closed-app/background activation is still pending.

This increment provides **manual foreground arrival checks**, not background geofencing. Open **Tools > Location reminders** in the normal app. No location request, permission prompt, sampling timer or location listener starts at startup, on resume or merely by opening this page. Phase 14B.1 native registration requires a separate explicit Start in a packaged Windows build. Background activation and restart/boot recovery remain later work. Earlier provider/native acceptance gates remain open.

## Try the feature

1. Create a pending device task, or sign in and cache an account task without a sync conflict.
2. Open Location reminders, choose Device or SyncedAccount, then Refresh. Account places are local to this installation and API-origin/account partition; they are not synchronized.
3. Choose New place reminder, a pending task and a place name. Enter decimal latitude/longitude using a decimal point and a whole radius from 100 to 5000 meters. Alternatively choose Use current location for draft and confirm the contextual explanation. This requests one fresh location fix; it does not save the sample automatically.
4. Choose Save and arm / rearm and confirm. Each arm starts with no evidence of arrival. Editing an existing reminder retains its task identity; use New for another task. Rearming captures the current task title and repeat occurrence.
5. Use Allow/check notifications and confirm before checking arrivals. OS settings can still prevent delivery even when permission is reported allowed.
6. While reliably outside the saved radius, use Check arrivals now and confirm. Later, while reliably inside, explicitly check again. Each check obtains at most one location fix for all eligible reminders in the selected workspace. Starting inside does not notify.
7. The inside transition consumes one generic notification **attempt** per arm. Review saved status with Refresh; explicitly rearm for another arrival. Disable opts out; Delete also removes the saved place. Neither action deletes/completes the task.

Leave the page to cancel current work and clear the coordinate draft. Window deactivation/stopping also cancels current work. A native permission dialog may interrupt an in-progress request; after granting access, return and manually retry. Cancel does not undo already committed storage or an OS delivery. A write can finish after cancellation; Refresh shows the durable result. Existing enabled places remain armed until disabled/deleted or invalidated by a later task check.

## Deterministic arrival rules

LocationRules uses Haversine distance on a mean-radius spherical Earth, including antimeridian crossings. A sample must be no older than two minutes, no more than ten seconds in the future, not marked as mocked, and have finite positive reported accuracy at most min(500 m, radius / 2). Current-location drafting accepts fresh nonmocked fixes with reported accuracy up to 500 m. Requested High accuracy is a preference, not a guarantee.

- Inside: distance + reported accuracy <= radius.
- Outside: distance - reported accuracy >= radius + 50 m.
- Other samples are uncertain and do not advance the state. The extra outside margin reduces boundary jitter. Reported accuracy is an estimate, not a physical containment guarantee.

State progresses Armed -> OutsideObserved -> Attempted. There is no automatic rearm, dwell detection, exit alert, trajectory inference or sampled location history. Outside evidence persists across app restarts until an explicit rearm. Manual samples do not establish a continuously observed crossing. Stale/approximate/unavailable fixes can prevent an arrival alert; use ordinary task reminders for time-based needs.

On Refresh/check, missing/completed/conflicted tasks or a different repeat rule/occurrence disable the old arm as TaskChanged. A normal rename retains the saved title snapshot. Cached account tasks can be used offline while the saved Todo session remains unexpired; server changes not yet downloaded are unknown. No source task, calendar block, sync outbox or provider event is changed.

## Protected local persistence

location.db3 is an independent schema-1 SQLite file. LocationReminders stores clear IDs, workspace partition, positive revision and unique negative notification ID, plus an AES-256-GCM encrypted JSON payload. Task title, task identity/recurrence, place name, coordinates, radius and state/status are encrypted. Associated authenticated data binds each payload to its workspace, row ID, revision and native ID. Metadata holds only a key fingerprint and decreasing negative ID counter. Negative IDs never overlap Phase 11's positive IDs and are never reused after deletion.

The 32-byte key is kept only in platform SecureStorage (`todo.location.encryption.v1`). Missing/mismatched keys, tampered payloads, invalid metadata or newer schema fail closed without a reset or plaintext fallback. Keep the original database and OS credentials together. A copied database alone is insufficient for recovery. Backup, OS credential restoration and reinstall behavior require device acceptance; uninstall/key loss may make places permanently unreadable. SQLite/filesystem deletion is not guaranteed secure erasure. OS/device compromise is outside this protection boundary. Debug workspace preview disables protected place storage and location rather than using real OS credentials.

No individual position fix or travel history is persisted, logged by this feature, added to notification payloads, sent to Google, or uploaded to the Todo API. Native location services remain subject to the operating system's own privacy policy. The saved place center is intentionally retained until deletion. Notification adapters receive only an integer ID and show existing generic text.

SQLite transactions enforce revisions, scope isolation, a 100-place limit per workspace and globally unique negative IDs. Attempted is saved **before** the native show call: failure/crash can lose an alert, but cannot automatically retry that arm. Disabled is saved before cancellation; deletion follows successful cancellation. If cancellation fails, keep/reload the disabled record and retry Disable/Delete. Session/version/cancellation guards reject old asynchronous results and queued actions. Repeated task/saved-row checks and compare-and-swap prevent stale checks from consuming a replacement arm.

The SQLite commit and OS notification API cannot form one atomic transaction. Cleanup after an in-flight account/window/row change is best effort; a concurrent change or native crash can still race an already submitted generic alert. This is not an exactly-once delivery guarantee. Native receiver/background callbacks are not registered for location in 14A.

## Platform behavior and acceptance

The native adapter uses MAUI Geolocation.Default.GetLocationAsync with High accuracy, a 20-second timeout and cancellation. It never calls GetLastKnownLocationAsync, foreground listening APIs or requests LocationAlways. The call occurs only after an explicit contextual confirmation. Android declares coarse/fine permissions and optional hardware features; no background-location permission is added. Apple sources include the when-in-use purpose string and Mac Catalyst sandbox location entitlement. Microsoft's current MAUI documentation requires no additional Windows setup; permission support must be judged from the actual location request, not a generic permission check that always returns Granted on unsupported platforms. See [MAUI geolocation](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/device/geolocation?view=net-maui-10.0) and [MAUI permissions](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/appmodel/permissions?view=net-maui-10.0).

Only the Windows target is compiled in this environment. Portable tests use fake fixes/notification adapters and real temporary encrypted SQLite files. No real location was read and no native location/notification permission was operated during implementation. Before release, verify:

- Actual Windows screen, keyboard/screen-reader flow, foreground OS permission, denied/revoked access, location-services off, inaccurate/approximate fixes, timeout and generic notification delivery/focus settings.
- Restart with the original key, missing/corrupt key recovery messaging, preserved state, outside/inside sequence and explicit rearm; opt-out/delete and failed cancellation retry.
- Account expiry/switch, page/window departure, permission-dialog cancellation, multiple windows/processes, task completion/deletion/conflict/occurrence changes and concurrent native delivery.
- Android build/device permission, approximate location and background/force-stop restrictions; Apple build/signing/entitlements/device permission. Current source configuration is not mobile runtime verification.
- Review platform-specific background geofence APIs, registration quotas, boot/reinstall recovery, permissions and battery policy before enabling Phase 14B. There is no closed-app arrival detection in 14A.

Implementation verification (2026-11-14): 40 new location cases pass; full suite 569 passed, 0 failed, 0 skipped (510 portable plus 59 PostgreSQL). Final Windows solution/XAML build: zero warnings/errors. Native/device checks above remain pending.
