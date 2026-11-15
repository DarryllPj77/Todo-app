# Phase 16E: selected Android identity evaluation

Implemented 2026-11-15 (Asia/Manila). **Opt-in evaluation only; no production signing or distribution.** The selected package is io.github.darryllpj77.todoapp. The existing development profile remains the default. No APK was installed and no application data was moved or cleared by this increment.

## Build the selected package

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Android.ps1 -IdentityProfile Selected -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Android.ps1 -IdentityProfile Selected -Configuration Release
```

The helpers require the matching user-confirmed application ID in config/android-release-plan.json. Distribution/custody can remain pending for these SDK-development-signed evaluations; those decisions are still required before production signing/distribution. The plan remains PlanningOnly and ReleaseReady=false. No key is generated or selected for production.

| Property | Development (default) | Selected (opt-in) |
| --- | --- | --- |
| Application ID | com.companyname.todoapp.maui | io.github.darryllpj77.todoapp |
| App label | TodoApp.Maui | Todo App (identity test) |
| Version code/name | 1 / 1.0 | 1 / 1.0.0 |
| Debug artifact root | .tools/android-build | .tools/as-d |
| Release artifact root | .tools/android-rel | .tools/as-r |
| Debug report | .tools/android-audit/debug-report.json | .tools/android-audit/selected-debug-report.json |
| Release report | .tools/android-audit/release-report.json | .tools/android-audit/selected-release-report.json |
| Signing | SDK development key | SDK development key |

Within each artifact root, the APK is at `bin/TodoApp.Maui/<debug-or-release>_net10.0-android/<application-id>-Signed.apk`. Build settings remain at build-settings.json under that root. Debug/Release trimming/AOT and signer semantics are described in ANDROID_RELEASE_EVALUATION.md. No switches still mean Development/Debug; -IdentityProfile Development can be supplied explicitly. Short isolated roots avoid the native Windows path-length issue and prevent profile output collisions.

Audit existing selected outputs with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidPackage.ps1 -IdentityProfile Selected -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidPackage.ps1 -IdentityProfile Selected -Configuration Release
```

Selected audits require profile-bound build settings for either configuration. Existing older Development settings can still be audited through the documented compatibility path. A default Development audit must reject a selected-package APK, and the reverse must fail too. Schema-3 reports record identity profile, component namespace, package/label/version, identity-policy hash and selected-plan hash alongside the existing artifact/settings/audit hashes. These are local evaluation records, not production approval or provenance guarantees.

## Deliberate package/component mapping

The conditional MAUI profile changes Android application ID, version name and test label only when Selected is requested. Unknown profile names fail the Android build. The default project ApplicationId remains the development ID, so the planning checker inspects that unconditional baseline rather than treating the conditional entry as an accidental rename. Windows does not select the Android-only overrides/resources.

Native Java component declarations and the fixed capture action deliberately retain their existing com.companyname.todoapp.maui namespace. A Java class name is not the APK's application ID. Context/type-bound PendingIntents resolve within the creating app; this change does not redirect widget/geofence operations into the development app. Both manifest audits check the expected explicit component names, private receiver boundaries and absence of sharedUserId. Provider authorities must be scoped to the audited application package, avoiding a conflicting authority across installations. Runtime coexistence still needs device acceptance.

The selected launcher shortcut is a separate conditional AndroidResource with the same logical XML resource name. Its targetPackage is the selected application ID, while targetClass and action identify the retained native capture component. The development shortcut keeps its original target. Audits inspect the actual packaged XML variants, so a selected shortcut accidentally targeting the development app fails before any install. The resource alias follows [Microsoft's AndroidResource LogicalName documentation](https://learn.microsoft.com/dotnet/android/building-apps/build-items).

AndroidIdentityProfiles.psm1 is an explicit two-profile allowlist shared by build/audit tooling. It does not infer a trusted package from the APK being checked or accept an arbitrary package-name argument. It validates the selected plan choice before selected evaluation. Existing permissions, backup/network policy, domain data model and schemas are unchanged; no public component or trust exception was added.

## Data and installation boundaries

The selected APK is a different app sandbox. It does not update, recover or silently copy development tasks, unsent outboxes/receipts, settings, protected credentials, encryption keys, permissions or reminder choices. Keep the current app/data until a separate transition is designed and accepted. Never uninstall the only copy of device-only/unsynced data merely to make an install succeed. Android managed backup remains excluded as documented in ANDROID_DATA_POLICY.md.

Already uploaded account tasks can be downloaded after normal sign-in/verification against the same server. Device-only tasks and an unsent queue are not recovered that way. No import/export, cross-package file/key copy, sign-in, native registration or automatic migration is implemented. Two independently opted-in reminder installations can produce independent notifications; review old-app opt-ins explicitly when later testing coexistence.

The test label helps distinguish the candidate. For manual tests, confirm the package in Android app information as well as the exact audited APK hash. Use disposable data, exercise launcher/share/widget entry, Save/Cancel and explicit account selection separately in each app, and prove one app's edits do not change the other's device workspace. Also verify upgrade within each compatible identity, permissions, provider authorities, Stop/restart and Release-mode persistence/reflection. No such native result is claimed here.

## Verification limits

The extended package suite has 92 passing cases, including cross-profile rejection, selected metadata, shared UID/provider authority conflicts, profile-bound settings and unconfirmed/mismatched selected-plan choices. The 33 plan cases still pass. Compiled artifact/build evidence is recorded in DEVELOPMENT_PROGRESS.md.

The full application regression attempted during this increment was blocked before discovery by Windows Application Control while loading TodoApp.Domain.dll (0x800711C7; Code Integrity events 3033/3077). It is not a new 708-test pass or an application assertion failure. The prior successful application baseline remains Phase 16B. Policy was not disabled, files were not unblocked or relocated to evade it, and no test-loader workaround was used. An administrator-approved resolution for the trusted local test binaries and a rerun of the original suite are needed for current core-regression evidence.

ReleaseReady remains false. Actual phone coexistence/Release startup/data/recovery/TLS/accessibility/geofence/provider behavior, distribution/custody and approved production signing remain open. See ANDROID_SIGNING_PLAN.md and ANDROID_RELEASE_READINESS.md.
