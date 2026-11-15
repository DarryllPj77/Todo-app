# Phase 16F: physical Android acceptance

Session date: 2026-11-15 (Asia/Manila). **In progress; installation, visible startup, device-only Save/reopen persistence and main-editor Cancel passed. Launcher/share/widget and other native cases remain pending.**

The user selected their physical Android phone, private APK testing, and project-owner signing-key custody. These decisions are recorded in config/android-release-plan.json. Production key creation, recovery, distribution and release approval remain pending; Stage=PlanningOnly and ReleaseReady=false.

## Device and artifact

- Source baseline: main, 5a2f6b9 (Phase 16E). Public planning decisions changed during this session; the existing APK was audited again before installation.
- Physical device model: 2311DRK48G; Android 15 / API 35; arm64-v8a. USB debugging authorized. Device serial omitted.
- Package: io.github.darryllpj77.todoapp; label: Todo App (identity test); Debug evaluation, SDK development signer.
- APK: .tools/as-d/bin/TodoApp.Maui/debug_net10.0-android/io.github.darryllpj77.todoapp-Signed.apk
- SHA-256: AC79490004B0077E7C7EA12258D3B5F261B1652C00CA70045430291F717B97BA
- Selected Debug artifact audit: passed before install. This is static artifact evidence, not native acceptance.
- Neither the Development nor Selected package was installed before the attempt.

## Actual results

| Case | Expected | Actual | Status |
| --- | --- | --- | --- |
| USB connection | One authorized target | One authorized physical device; Android/API/ABI read successfully | Passed |
| Fresh Selected Debug installation | Package installed without deleting data | Initial attempt returned INSTALL_FAILED_USER_RESTRICTED; after the user reported approval, a streamed retry returned Success and package inventory confirmed the selected ID | Passed |
| Cold launcher handoff | Android starts the selected main activity | am start -W returned Status: ok, LaunchState: COLD, TotalTime: 2032 ms; process present immediately afterward | Passed (OS handoff only) |
| Visible startup | App screen renders | User confirmed: The Todo app screen is visible | Passed (user observed) |
| Device-only Save and reopen | Disposable task appears and remains after closing/reopening | User confirmed Phone smoke test appeared after Save and remained after swiping the app away from Recents and reopening | Passed (user observed; duplicate count not explicitly confirmed) |
| Main-editor Cancel | Discarding an unsaved draft adds no task | User entered Cancel test, tapped Cancel and confirmed it did not appear in the list | Passed (user observed) |
| Launcher shortcut Save/Cancel | Correct app and destination; Cancel adds nothing | Not run | Pending |
| Share/widget entry | Correct selected app; explicit Save | Not run | Pending |
| Release startup/persistence | Audited Release artifact works on device | Not run | Pending |

The installation restriction cleared after user approval; no protection bypass or device/account-setting change was performed by the assistant. The selected main activity was launched. No runtime permission was granted, data cleared or production key accessed. This was a local development-signed USB test install, not production distribution.

After installation, use disposable device-only tasks first; confirm Save, Cancel and persistence through user observation. Record launcher, actions and actual results before marking each case passed. Coexistence, same-identity upgrades, interrupted saves/recovery, TLS, accessibility, geofences and real providers remain separate pending cases. The Windows Application Control block on the core test loader also remains unresolved; no new full-suite pass is claimed. See ANDROID_RELEASE_READINESS.md and ANDROID_IDENTITY_PROFILE.md.

The updated public signing plan passed structural validation and all 33 plan tests. These checks do not establish native acceptance or production readiness.
