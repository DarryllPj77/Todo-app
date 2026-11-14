# Phase 16A: Android release-readiness preparation

Updated 2026-11-15 (Asia/Manila). **Release readiness: BLOCKED.** This increment automates inspection of the existing development APK and records the remaining acceptance work. It does not create a release candidate, change backup behavior, install an app, access a phone or approve distribution.

## Reproduce the checks

From the repository root in Windows PowerShell 5.1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/AndroidPackageAudit.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Android.ps1
```

The build now runs the APK audit after successful debug signing. To inspect an already built APK without rebuilding:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidPackage.ps1
```

The existing isolated .tools Android SDK/build-tools 36.0.0 and JDK are required. No additional package/Pester/Python dependency is added. Optional `-ApkPath` accepts an APK to inspect; `-ReportPath` must be a separate JSON file. Defaults resolve relative to the repository, independently of the caller's working directory. Invalid argument types are rejected before report writing. Tool/input/check failures terminate with a nonzero PowerShell process exit and write a failed report when report arguments are valid. All native commands use argument arrays, without evaluating command text.

The report defaults to ignored `.tools/android-audit/debug-report.json`. It records UTC check time, APK SHA-256, audit script/module SHA-256, package/version/SDK values, signature schemes/warning count, permissions/exported components, backup/transport attributes, embedded assembly/ABI presence, checked resource paths and open release gates. It writes through a temporary JSON file and replaces the prior report, including on failure when the output path is writable, so a failed inspection replaces the prior result for that path. If report writing itself fails, the command fails; an older report is not evidence of the current attempt. Separate report paths preserve separate evidence. No task, account, token, location or provider data is read.

**Exit 0 means only that the debug artifact matches this audited baseline.** `ArtifactStatus=Passed` always coexists with `ReleaseReady=false` in this preparation tool. The script deliberately requires a debuggable APK and must not be presented as a release-candidate validator. It verifies cryptographic signatures but does not establish signer trust: no approved release certificate is pinned. The hash identifies APK bytes, not a clean source revision or a reproducible byte-for-byte build. Script/module hashes identify the policy used; rerun after any artifact or policy change.

## Automated scope

- Verify APK signatures with apksigner, requiring v1/v2/v3 and one signer. Record AndroidX META-INF/JAR coverage warning count without suppressing or claiming to resolve those warnings.
- Inspect the compiled APK manifest with aapt2, rather than trusting source XML. Check the current application ID, minimum API 21/target API 36, debug flag, exact permission set/signature-protected internal permission and reviewed exported component boundaries.
- Require the narrow single ACTION_SEND/DEFAULT/text/plain capture filter, empty capture task affinity/Recents exclusion, private widget and background-transition receivers, expected boot/update actions and DUMP-protected profile installer when present. Unexpected public components or permission expansion fail.
- Resolve resource IDs from the APK resource table and check the actual packaged launcher/widget XML variants, widget layout/click ID/accessibility reference, zero refresh, generic preview, modern cell hints and fixed shortcut action/package/class. This does not execute the Activity/PendingIntent or prove runtime privacy behavior.
- Check app/domain/infrastructure assemblies are embedded for arm64-v8a and x86_64. This verifies the current standalone debug format, not every possible ABI/distribution format.
- Check the APK hash again after inspection to reject an artifact changed during the audit. No installation, emulator, SDK download/license operation, provider write or runtime permission request is performed.

The parser normalizes the Android attribute namespace into a DOM and rejects unknown output forms. A toolchain/resource/application-ID/SDK change can therefore require a reviewed policy update. Do not widen checks just to make a new package pass. Run the policy tests, build/audit and relevant native acceptance after such changes.

## Open acceptance and release gates

| Gate | Confirmed current evidence | Required before closing |
| --- | --- | --- |
| Backup and transfer | Compiled APK has allowBackup=true; no explicit fullBackupContent or dataExtractionRules. Local task/sync text is not independently encrypted. | Define cloud/device-transfer policy for task data, protected credentials and encrypted journals; implement supported Android-version rules and test real restore/lost-key behavior. Phase 16B is proposed for this policy work. |
| Transport configuration | Client session validation requires an HTTPS origin; compiled app has no explicit usesCleartextTraffic/networkSecurityConfig. | Review/declare the intended Android transport policy and test endpoints/certificate failures. No certificate bypass or production trust exception is authorized. |
| Identity and signing | Development ID com.companyname.todoapp.maui, version 1/1.0, debug signing; signatures verify. | Choose durable release identity/versioning and approved certificate/distribution plan, protect keys outside Git, verify certificate identity and upgrade compatibility. |
| Capture on phones | Portable capture tests and APK resource checks pass. | Record real launcher/share/widget behavior, multiple instances/resizing, offline saves/account changes, interrupted Save/recreation/task stacks and supported API/host combinations. See ANDROID_QUICK_CAPTURE.md. |
| Geofences/notifications | Portable permission/lifecycle/crypto tests and native compilation pass. | Perform ANDROID_GEOFENCES.md device matrix, actual dwell/exit/Stop/restart/revocation/OEM battery cases and notification delivery/privacy. |
| Accessibility | Native labels/layout exist; no phone acceptance evidence. | TalkBack, switch/keyboard, contrast, large text, small screens, focus/errors and widget tap area on selected devices/launchers. |
| Database recovery | Transactional migrations/rollback/idempotency are covered by existing automated tests. | Prove upgrade from supported app versions, interrupted writes, storage exhaustion and controlled backup/restore without silent reset; preserve queued operations/receipts and account boundaries. |
| Provider and distribution | Google provider, background-location justification/privacy and Play/release acceptance remain open. | Perform the documented real-provider cases and complete distribution disclosure/review for the selected features. No store submission is performed here. |
| Other platforms | Apple support and earlier Windows native/MSIX resource-warning gates remain pending. | Use Mac tooling for Apple; finish selected Windows package/device checks separately. Android preparation does not close these gates. |

For each manual case, record app commit and APK hash, device/OS/API/launcher or provider, prerequisites, exact actions, expected/actual result, pass/fail, date and redacted evidence. Keep tokens, passwords, coordinates and personal task text out of committed logs. No manual case is marked passed in Phase 16A. Compilation, static audit or an empty checklist is insufficient evidence.

Android's [Auto Backup documentation](https://developer.android.com/identity/data/autobackup) distinguishes modern extraction rules and older backup formats, and notes that allowBackup=false alone can leave OEM device-transfer behavior enabled. Treat a single manifest flag as incomplete restore/privacy evidence. Native inspection follows [aapt2](https://developer.android.com/tools/aapt2) and [apksigner](https://developer.android.com/tools/apksigner) documentation.

## Recovery and upgrade boundaries

1. Keep test and production accounts/data separate. Prefer upgrade installation with the same application ID and signing identity when testing retention. If installation reports a signature mismatch, investigate it; do not uninstall merely to bypass it, because uninstall can remove device-only tasks, unsynced changes and protected keys.
2. Schema-7 device/cache files must not be opened with older app versions that reject them. Use a compatible app to inspect/recover; do not delete/reset a database to make an older binary start. APK rollback and database downgrade are separate operations.
3. For an uncertain capture Save, inspect the selected workspace before starting another capture. The same-screen retry keeps a capture ID/receipt; a new screen/share/widget tap has a new ID and is not content-deduplicated. An account outbox remains local until normal verified sync succeeds.
4. A missing/corrupt protected location key is not repaired by copying another user's key or clearing encrypted files. Keep the documented fail-closed behavior, use explicit Stop/block controls where available, retain encrypted evidence and follow the location/background recovery guides before relying on delivery.
5. Do not assume copying a live SQLite file is a consistent backup: journal/WAL activity, protected credentials, partition ownership and related files matter. This increment implements no export/import or automatic restore feature. Define and test a consistent recovery procedure before promising data portability.
6. An audit failure preserves the APK; inspect its failed report, confirm the toolchain/artifact and fix the underlying drift. Do not treat a report for an older hash as evidence for a new APK. Clearing a report is not a repair.

## Phase 16A verification

The PowerShell policy/parser suite contains 23 passing cases, including unknown public receivers/aliases, private worker/widget export changes, broadened share filters/URI routes, permission expansion, SDK/package changes, missing boundaries and malformed/sibling aapt2 trees. A deliberately invalid APK was rejected with a nonzero process exit and a failed JSON report. The real debug APK and integrated build/audit results are recorded in DEVELOPMENT_PROGRESS.md. Native phone/release outcomes remain pending.
