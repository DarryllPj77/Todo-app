# Phase 16C: Android Release-mode evaluation

Implemented 2026-11-15 (Asia/Manila). **This is development-signed build validation, not a distributable release candidate. ReleaseReady remains false.** No app installation, publishing, store submission or production key operation is performed by the helper.

## Identity dimension added in Phase 16E

The original paths/table below describe the default Development identity. Add -IdentityProfile Selected for the user's chosen package in separate .tools/as-d / .tools/as-r roots and selected-* reports. Selected uses a confirmed plan choice, a distinct test label and package-specific shortcut/authority checks while retaining native component names; see ANDROID_IDENTITY_PROFILE.md. Both identity profiles remain development-signed and not release-approved.

## Build and inspect both configurations

From the repository root in Windows PowerShell 5.1:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/AndroidPackageAudit.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Android.ps1 -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Android.ps1 -Configuration Release
```

Configuration defaults to Debug. Both modes explicitly use AndroidKeyStore=false, the SDK development-signing path. The helper accepts no production-keystore/password parameters and does not create a production identity. Do not distribute these APKs as approved releases just because the configuration is named Release.

| Evidence | Debug | Release evaluation |
| --- | --- | --- |
| Artifact root | .tools/android-build | .tools/android-rel |
| Standalone APK suffix under root | bin/TodoApp.Maui/debug_net10.0-android/com.companyname.todoapp.maui-Signed.apk | bin/TodoApp.Maui/release_net10.0-android/com.companyname.todoapp.maui-Signed.apk |
| Evaluated property evidence | build-settings.json under artifact root | build-settings.json under artifact root |
| Audit report | .tools/android-audit/debug-report.json | .tools/android-audit/release-report.json |
| Debug flag | Must be explicitly true | Must be absent or false |
| Required assembly payload | Individual nonempty app/domain/infrastructure ELF entries for both ABIs | Nonempty Mono assembly-store container and app/domain/infrastructure native AOT modules for both ABIs |

All outputs stay ignored. Both profiles target the existing Android ARM64/x64 ABIs; a minimum API level does not imply every CPU architecture is packaged. The shorter Release root avoids native AAPT Windows path-length failures in this checkout. Do not rename it to a long nested path without rebuilding resource compilation.

To audit existing outputs without rebuilding:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidPackage.ps1 -Configuration Debug
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidPackage.ps1 -Configuration Release
```

Release audit requires build-settings.json for the same APK hash and configuration. A custom APK can be paired with explicit -BuildSettingsPath and -ReportPath. Debug standalone inspection can omit settings for compatibility with earlier artifacts; the build helper always records them. Use separate report paths when preserving earlier evidence. Build failure does not turn an older report into evidence of a new attempt.

## Optimization and build evidence

The current .NET 10/Android workload Release defaults remain enabled: Mono runtime, PublishTrimmed=true, TrimMode=partial, RunAOTCompilation=true, AndroidEnableProfiledAot=true, AndroidUseAssemblyStore=true and JsonSerializerIsReflectionEnabledByDefault=true. The latter matters because the current persistence/DTO code uses reflection-based JSON. No warning suppression, trimming disable, interpreter fallback or NativeAOT conversion is used to make this build pass.

The helper records evaluated properties with the APK hash, and the audit checks the expected Release baseline. This is local build evidence, not cryptographic source provenance. It also inspects the actual APK for the current Mono-format libassemblies.&lt;ABI&gt;.blob.so containers and nonempty libaot-TodoApp.*.dll.so modules. Individual managed assembly entries inside the compressed stores are not decoded by this tool. Presence of containers/AOT modules does not prove every reflected member or runtime flow works after trimming.

UseDefaultPublishRuntimeIdentifier=false prevents the desktop .NET SDK from adding the Windows host publish RID when PublishTrimmed is enabled. Android still supplies its own android-arm64/android-x64 RIDs. The Release audit verifies those evaluated RIDs; this does not disable trimming or AOT. A raw escaped multi-RID override is not needed and must not be propagated as a single literal RID to referenced projects.

These settings and formats are specific to the selected SDK/workload. Future runtime/SDK changes require a reviewed baseline update and native regression, not merely a broader filename pattern. See Microsoft's [Android build properties](https://learn.microsoft.com/dotnet/android/building-apps/build-properties), [MAUI compilation/runtime guidance](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0) and [Android CLI publishing guidance](https://learn.microsoft.com/en-us/dotnet/maui/android/deployment/publish-cli?view=net-maui-10.0).

## Public signer identity and optional pinning

apksigner verifies v1/v2/v3 and one signer, then the audit records SignerCertificateSha256 from the certificate. That fingerprint is public identity metadata, not a signing password or private key. It is separate from the APK SHA-256, which identifies package bytes.

Both scripts accept optional -ExpectedSignerSha256 with exactly 64 hexadecimal characters and no colon separators. Obtain the expected value from your independently reviewed reference. Supplying an APK's own freshly observed fingerprint is not independent verification. A recorded earlier development APK can establish development-key continuity only; it does not approve a production certificate.

For example, after separately reviewing a reference, store its public SHA-256 in a PowerShell variable and pass -ExpectedSignerSha256 $expectedSigner to either helper. A mismatch terminates with a failed report. A match means only that the artifact uses the supplied identity: SignerTrust explicitly leaves approval/provenance outside this audit, and ReleaseReady remains false. No certificate pin is hardcoded in the repository because SDK development keys vary by machine.

Do not paste passwords/private keys into commands, reports, documentation or Git. Permanent application ID/versioning, signing ownership, key storage/backup/rotation, allowed upgrade path and distribution workflow still require a concrete approved plan. Changing ID/signing identity is not a harmless way to replace an installed app; preserve the data-loss/upgrade boundaries in ANDROID_DATA_POLICY.md.

## Identity plan after Phase 16D

The user-selected future package, pending channel/custody and upgrade/signing contract are recorded in ANDROID_SIGNING_PLAN.md and config/android-release-plan.json. Existing helpers keep the development identity as default; Phase 16E adds the explicit selected identity evaluation, still with SDK signing. A structurally valid plan does not switch a build profile or certify a production key; the plan checker itself renames/publishes no package.

## Verification and manual gates

The PowerShell suite now has 78 passing cases. Added cases reject Debug/Release confusion, missing/duplicate/malformed signer identity, a wrong expected fingerprint, another artifact's settings, altered optimization/runtime/signing defaults and missing or wrong assembly-store/AOT/ABI layouts. Existing permission/component/capture/backup/transport checks apply to both modes.

Automated build/package validation does not run the app. On selected physical test devices, exercise the Release-mode APK through startup/navigation/DI, reflection-backed JSON persistence, SQLite load/write/migrations, account restore/verification/offline sync, capture retry/recurrence/calendar/focus, launcher/share/widget activation and selected reminder/location flows. Compare installed upgrade/data retention only with compatible identity/signing, using disposable test data. Record crashes/linker-related missing members and actual results against the exact APK hash.

Phone/accessibility, actual TLS/backup/restore/OEM behavior, provider acceptance, approved release signer, permanent identity, distribution/background-location review and earlier Apple/Windows gates remain pending. Release-mode compilation is one completed preparation step; it does not close ANDROID_RELEASE_READINESS.md's acceptance matrix.
