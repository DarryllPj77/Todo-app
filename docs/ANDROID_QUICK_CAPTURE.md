# Phase 15A: Android launcher quick capture

Implemented on 2026-11-14 after the user selected Android quick capture ahead of Apple tooling. Phase 14B.4 and earlier device/provider acceptance remain pending. This is the first Phase 15 surface; Windows hotkeys, share targets, widgets and browser extensions are future work.

## Try it yourself

1. Build the standalone debug APK with `powershell -ExecutionPolicy Bypass -File scripts/Build-Android.ps1`. The existing Android workload, SDK and JDK are required; see [ANDROID_GEOFENCES.md](ANDROID_GEOFENCES.md) for setup. This command builds only; it does not install or launch anything.
2. Install `.tools/android-build/bin/TodoApp.Maui/debug_net10.0-android/com.companyname.todoapp.maui-Signed.apk` on your test phone using your chosen Android tooling.
3. On Android 7.1/API 25 or later, long-press the app icon in a launcher that supports app shortcuts, then choose **Add task**. Older Android versions still support the normal app editor but do not expose this shortcut.
4. Enter a title and optional description. **Device only** is the default and works without sign-in or a token-store read. Tap **Save task**, then open the main app's device task list to check it.
5. To capture for an account, first sign in through the main app. In capture, tap **Choose current account**, review the displayed email and API origin, and Save. This writes to that account's local sync queue without an HTTP request. Open the normal synced workspace and complete its usual verification/sync flow to upload it. The separate online workspace will only show it after synchronization.
6. **Cancel** or Back discards a draft that has not been submitted. The screen closes after a confirmed save. Draft text is not copied into an intent or saved-instance Bundle; leaving/recreating the activity can discard it. A Save already submitted may finish even if Android closes the screen; check the workspace before starting another capture.

Native phone behavior is not yet accepted. No phone/emulator installation, permission change or launcher interaction was performed by the assistant.

## Design and data flow

`MainActivity` declares a static `android.app.shortcuts` resource. Its generic Add task entry explicitly starts `QuickCaptureActivity` with one fixed action. The exported capture activity ignores every intent extra; no external title, account, ID, URL or token is imported. Other actions close immediately. The entry opens a compact native form without creating the normal MAUI window, attaching its runtimes or navigating away from its editor. Its separate empty task affinity keeps launcher shortcut task flags away from the main app task, and it is excluded from Recents.

The native view delegates to the portable `QuickCaptureViewModel` and `QuickCaptureService`. Domain `TaskItem` validation trims title/description and enforces 200/4000-character limits. Captures are pending, normal priority, without due date, category, tags, parent, prerequisites, quadrant assignment or recurrence. Advanced fields remain available in the normal editor. No new permissions, packages or server endpoint are required. Capture does not start/rearm a geofence run or request a position/notification permission; existing background reminder policy still applies.

Account selection explicitly restores only an unexpired saved session matching the configured HTTPS origin. Offline capture permits an unverified restored session because it writes only that user's local partition; it does not grant server access. The selected origin/user/session generation is pinned. The service checks it and expiry inside the SQLite operation and again before commit. Sign-out, expiry or endpoint/account replacement rejects the save instead of silently targeting another workspace. Device saves do not restore credentials. Native UI displays a generic recovery message; drafts/errors are not logged.

## Retry and migration behavior

Each capture screen creates one random capture/task ID. The first valid Save freezes both normalized fields and destination. While saving, controls are disabled. Storage failure keeps the same ID and draft for **Retry same save**, with destination and text locked; close and check the workspace before starting another capture. Validation errors remain editable. A confirmed save cannot be repeated from that view model.

Device task schema **7** adds `QuickCaptureReceipts(Id,Fingerprint)`. The task and its SHA-256 normalized-payload fingerprint receipt commit in one transaction. Account sync schema **7** adds `CaptureReceipts` to each replica partition; the new task, immutable outgoing operation and receipt commit together. Retries with the same payload confirm the receipt without overwriting later edits, replacing an outgoing operation or resurrecting a deleted task. Reusing an ID with different fields fails. Receipts survive task deletion; they contain IDs/hashes, not a second plaintext draft. Existing task/cache files upgrade transactionally; newer schemas are rejected without reset. Older app versions reject schema 7, so do not downgrade against these files.

Receipts are retained up to 10,000 per device database or account partition. At capacity, new quick captures are refused and the normal editor remains available. There is no automatic receipt pruning, separate draft database, cross-workspace move or automatic sync. Server migrations remain eight and the sync wire protocol remains **6**. Normal task text retains the existing SQLite/backup protection limits; capture does not add database encryption or change Android's existing backup configuration.

## Verification performed

All 689 tests passed with no failures/skips (630 portable, 59 PostgreSQL), including 18 new capture cases. The Android standalone debug APK and Windows solution compile with zero warnings/errors. APK inspection confirms v1/v2/v3 signatures, embedded assemblies, explicit capture activity/task attributes, launcher metadata and versioned shortcut resources with labels/icon/target. apksigner emits AndroidX META-INF/JAR coverage warnings; release packaging remains a separate gate. No native phone/launcher outcome is inferred from these checks.

## Acceptance still required

- Install and use a compatible launcher on API 25+; check cold/warm entry, optional pinning, double-taps and preservation of the main editor/navigation stack.
- Save/cancel offline device drafts, whitespace/length boundaries, keyboard scrolling, rotation/background/process death and interrupted saves. Verify a committed save can be found after the screen disappears.
- Choose a restored account offline, confirm its email/origin, queue, reopen the synced workspace, verify and upload once. Test expired sessions, sign-out/API changes and multiple accounts without retargeting.
- Exercise TalkBack labels/live errors, large fonts, small screens, dark mode and keyboard-only/switch navigation on actual devices. The form currently uses the Android light dialog theme.
- Check external activation with other actions/extras; it must never prefill or auto-save. Review launcher OEM behavior, backup/restore and release signing separately.
- Complete the earlier Windows/Android geofence acceptance, Apple support and Google/provider gates independently. Compilation and portable tests do not establish these runtime outcomes.

The static entry follows [Android's official shortcut creation documentation](https://developer.android.com/develop/ui/compose/system/shortcuts/creating-shortcuts). Native Android types remain in Platforms/Android; the portable service, view model and real SQLite behavior are tested without an emulator.
