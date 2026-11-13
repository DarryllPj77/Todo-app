# Task reminders (Phase 11)

## Using reminders

Close the older app before rebuilding with `scripts/Run-Windows.ps1`. Create an incomplete device task, or sign in and download/create a cached task in Synced tasks. Open **Reminders** in the main task-list toolbar. Use **Allow / check notification permission** when you want notifications, choose a task and explicit date/time, then **Save / replace reminder**. Inspect the saved row's status; a saved choice is not proof of native scheduling or delivery.

The picker includes incomplete device tasks and the active account's cached tasks. The Phase 9 online-only workspace is not a reminder source until the task is downloaded into Synced tasks. DueDate alone never sends an alert. Reminders belong to this installation and are not copied through the server/sync protocol. A task without a due date can still have an explicit reminder.

Use **Refresh tasks & reminders** to load current rows. **Retry scheduling / cancellation** reconciles saved choices after fixing system settings or a native failure. **Cancel selected reminder** persists cancellation before native removal. Disabled rows remain visible for diagnosis. Select a future date/time to rearm a canceled, missed, or already attempted reminder; reopening a completed task does not silently rearm it.

## Date, time, and edit policy

The editor uses the device's displayed time-zone ID. Saving resolves the chosen local date/time **once** to an absolute UTC instant and retains the original wall time/zone for explanation. Traveling changes the local display of that instant, not the scheduled instant itself. Selecting a saved reminder for editing converts its UTC instant into the device's current local time. Saving then records that newly displayed zone/time.

Choose a time at least one minute ahead and no more than 365 days ahead. Invalid times skipped by a daylight-saving transition and ambiguous repeated times are rejected; the app does not guess which occurrence you intended. The existing DateOnly DueDate remains a calendar deadline, not a UTC midnight or notification time.

Task deletion/completion cancels and disables its reminder. A due-date change, including clearing or adding a due date, also cancels it for explicit review rather than guessing an offset. Title changes retain the reminder's instant and update its saved caption. Generic notification text contains no task title, account, origin, description, or bearer token. Clicking an alert opens/activates the app rather than selecting an account automatically.

Signing out or changing accounts/origins suspends the old account's notifications and hides those rows. Choices remain stored; returning to the same account may reschedule still-future enabled reminders. Device-only reminders are unaffected. Session-version checks cancel a stale schedule created during an account switch. Native cancellation is best effort, so inspect errors and retry if removal fails; an already displayed or imminent alert cannot be recalled with certainty.

## Platform behavior

| Platform | Implementation and current verification |
| --- | --- |
| Windows | Windows App SDK app notifications, evaluated by a 15-second application timer. Windows source/XAML compiled with zero warnings/errors. Actual display/registration/settings acceptance is pending. |
| Android | Notification channel, explicit POST_NOTIFICATIONS request where needed, inexact AlarmManager alarms, an explicit receiver, and reboot restoration of eligible future reminders. Source added; Android build/device acceptance is pending. |
| iOS / Mac Catalyst | UNUserNotificationCenter permission, pending-request replacement/removal, and foreground presentation delegate. Source added; Apple build/device acceptance requires the platform toolchain and is pending. |

The current Windows build is **unpackaged** and deliberately implements running-app reminders. Keep it running; there is no closed-app scheduler/service. A reminder due within the previous five minutes gets one delivery attempt when evaluated. Older missed reminders are recorded without an alert flood. The attempt is saved before calling the native show API, so a crash/uncertain show is not automatically retried. This trades guaranteed delivery for avoiding duplicate alerts. Re-arm with another explicit future time to try again. Sleep, system clock changes, notification settings, and Focus can affect what the user sees. Already attempted alerts do not repeat after clock rollback.

Windows registration uses AppNotificationManager; no new package or COM installer is added. The Windows App SDK may require its notification runtime dependencies even for self-contained apps, and elevated apps do not support app notifications. Run normally; registration/setup failures stay visible as unavailable/failed status instead of crashing task saves. See [AppNotificationManager requirements](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.appnotifications.appnotificationmanager?view=windows-app-sdk-1.8) and [Windows notification quickstart](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart). Durable Windows closed-app scheduling/packaging is a remaining platform milestone, not claimed by this development build.

Android uses inexact alarms to avoid exact-alarm special access; delivery may be delayed by Doze or battery policies. A boot receiver restores eligible future choices, while force-stop requires reopening the app. The receiver rechecks saved time, enabled state, active account metadata, and persisted task completion/due date before showing generic text. The manifest adds POST_NOTIFICATIONS and RECEIVE_BOOT_COMPLETED; it does not request exact-alarm permissions. See [Android alarm behavior](https://developer.android.com/develop/background-work/services/alarms) and [notification permission](https://developer.android.com/develop/ui/compose/notifications/notification-permission).

Apple uses OS local requests and removes pending/delivered entries by its own identifier. Foreground alerts use a retained delegate. Delivery still depends on user settings/Focus; see [User Notifications on .NET Apple platforms](https://learn.microsoft.com/en-us/dotnet/ios/app-fundamentals/user-notifications). Native permission denial never enables a plaintext token fallback or blocks task editing.

Only the nearest **32 future eligible reminders** are scheduled per reconciliation, using stable device-local numeric IDs. This is an app policy to bound scheduling, not a statement of every OS's maximum. Later rows show waiting status and are considered when the app runs again. No notification click actions, snooze, repeat alarms, server push, background sync, geofencing, or guaranteed closed-app replenishment is introduced.

## Storage and reconciliation

`reminders.db3` has independent schema version 1 and a ReminderSettings row containing the reminder state. Each entry stores its native ID, task scope/ID, caption, captured due date, UTC instant, original wall time/zone, enabled/attempted state, and diagnostic status. Native IDs are allocated transactionally, stable on replacement, and unique across device/account task keys. Corrupt or newer schema data fails without reset. Task captions/choices are ordinary unencrypted SQLite; tokens remain in SecureStorage and are never notification payloads. Android additionally remembers the active reminder account scope as nonsecret Preferences metadata for receiver checks.

ReminderTaskCatalog reads concrete device SQLite plus only the current account's sync cache. ReminderService serializes reminder transitions, checks task/session state, persists desired choices before native operations, and retains failures for retry. It never prompts from reconciliation. One native cancellation failure does not prevent trying other cancellations. Platform error/status observers cannot make a committed task save fail.

ILocalTaskService is decorated with ReminderAwareTaskService for post-commit reconciliation. SqliteSyncStore exposes an awaited, exception-isolated Written callback after its transaction commits; MAUI attaches reminder reconciliation there for cached edits, downloaded changes, conflict resolution, and acknowledgements. Reads do not raise the callback, avoiding recursion. The API/sync wire contract and existing task/sync schemas are unchanged.

ReminderRuntime restores the saved session and reconciles on window creation/resume, session changes, and a 15-second app timer while windows remain. This keeps task/account changes reflected locally; it is not a server or OS background worker. Remote changes made while this device is offline cannot cancel a scheduled alert until they reach its cache. Android's receiver can recheck its persisted cache, but cannot discover undownloaded server edits.

## Hands-on acceptance (pending)

1. On Windows, run normally and check Reminders permission/setup status. Choose a disposable task and a time two minutes ahead. Keep the app open and confirm one generic toast/Notification Center entry. Check activation by clicking it.
2. Replace the time, cancel, complete, delete, clear/change the task due date, and reopen the task. Verify the old alert is canceled, title-only edits keep its instant, and rearming always requires a new explicit choice after cancellation.
3. Deny notifications in system settings, save a choice, and confirm the row says blocked/unavailable while task CRUD still works. Re-enable and Retry scheduling for a still-future reminder. Confirm there was no automatic permission prompt at startup.
4. Close/reopen the app and confirm choices/IDs persist. On Windows test the five-minute catch-up window and missed status; do not expect closed-app delivery. Check sleep/resume and clock rollback do not cause repeated attempts.
5. For synced tasks, schedule under account A, sign out/switch to B or another API, and verify A's native alerts are canceled and rows hidden. Return to A and check still-future choices. Complete/delete remotely, sync the change, and verify cancellation after download.
6. On Android, build/install with its SDK/JDK/workload. Test Android 13+ allow/deny/channel disable, foreground/background alerts, Doze delay, reboot restoration, explicit cancellation, and reopening after force-stop. Verify receiver/component registration and app-data paths on a real device.
7. On iOS/Mac Catalyst, build with a compatible Mac/Xcode toolchain. Test permission denial/re-enable, foreground/background request delivery, replacement/cancellation, Focus, and app restart. Confirm the foreground delegate stays alive.
8. Test DST gaps/repeats, travel between time zones, 33+ future reminders, native failures, screen-reader labels, light/dark themes, and narrow/mobile layouts. OS delivery remains best effort.

Automated verification uses real temporary SQLite files and actual portable services/ViewModels with a fake notification adapter. It verifies policy/persistence, UTC/DST rules, permissions, task mutation hooks, caps, races, account isolation, uncertain attempts, and cancellation failures. Windows compilation verifies native API/XAML references. Neither proves a toast displayed or that mobile source compiled/ran. Follow this checklist before relying on native delivery.

Phase 12B adds tags to task storage and sync, but does not add tag names to native notification payloads or change reminder schema 1. A tag-only edit retains a reminder's instant. Completion, deletion, and due-date changes keep their existing cancellation policy.

Phase 12C.1 adds parent hierarchy. Parent-only moves trigger reconciliation through the device decorator or committed sync-store callback and retain reminder time. Parent completion affects only that task; children retain their own reminder/completion state. Notification text stays generic and contains no task/parent fields. See HIERARCHY.md for coordinated client/API upgrade and native acceptance.

Phase 12C.2 dependency-only writes reconcile device/account reminder state through the existing committed-write hooks and retain the reminder instant. Completion that fails dependency validation does not write task state or cancel a reminder. Reopening a prerequisite does not automatically change dependent completion/reminders. Notification payloads stay generic and reminder schema remains 1. Native delivery acceptance is still pending.

Phase 12D planning-only changes reconcile reminders after committed device/cache writes and retain the reminder instant. Urgency/importance is not inferred from due dates and does not schedule or reprioritize notifications. Payloads remain generic; reminder schema stays 1. Native delivery acceptance remains pending.
