# Phase 16D: permanent Android identity and signing plan

Prepared 2026-11-15 (Asia/Manila). **Planning only. The user selected io.github.darryllpj77.todoapp, private APK testing and project-owner signing-key custody; release is not approved.** These are recorded decisions, not evidence of production keys, recovery or distribution. See ANDROID_NATIVE_ACCEPTANCE.md for the separate physical-device evaluation session.

## Evaluation implementation after this plan

Phase 16E adds an opt-in Selected build/audit profile for the confirmed package, without changing the default Development identity or generating production keys. Its test label and isolated outputs make the candidate explicit; see ANDROID_IDENTITY_PROFILE.md. The public plan is read by the selected-evaluation guard, which checks the confirmed package only. Phase 16F records the user's channel/custody choices; certificate provenance, custody implementation, recovery and native acceptance still require evidence. Stage=PlanningOnly continues to describe production-readiness status.

## What the application ID means

The application ID is the package name Android uses to identify this app and its updates, such as `io.github.darryllpj77.todoapp`. It is not a phone/device identifier. It is also separate from the visible app label and C# namespace. A user can see a friendly name while Android uses the package name internally. This document proposes the name above from the repository owner's GitHub handle; it does not reserve it or prove availability in a store.

The current app is `com.companyname.todoapp.maui`, versionCode 1 / versionName 1.0, signed with this machine's SDK development certificate. The proposed permanent package would be a **separate installed app**, not an in-place update of that development package. It would not automatically inherit SQLite tasks, unsent sync operations, receipts, preferences, credentials, permissions, encrypted location data or reminder opt-ins. Keep the existing app/data while reviewing a transition; never uninstall it merely to bypass an identity/signature mismatch.

## Concrete proposal and open decisions

The public, versioned record is `config/android-release-plan.json`:

| Item | Proposed value | State |
| --- | --- | --- |
| Permanent package | io.github.darryllpj77.todoapp | Selected by the user; opt-in evaluation implemented in Phase 16E, not promoted/reserved/availability-verified |
| First distribution | Private APK testing first | Confirmed by the user in Phase 16F; no distribution performed |
| First permanent-package version | versionCode 1 / versionName 1.0.0 | Initial plan seed, not applied to the current app |
| Key custodian | Project owner, under their own control | Confirmed by the user in Phase 16F; no private key created or accessed |
| Private APK signer | Owner-managed long-term app-signing key | Proposed; no key/certificate exists in this plan |
| Key storage | Owner-controlled encrypted storage outside the repository | Proposed; no location/password recorded |
| Backup | At least two independent encrypted copies, with a recovery rehearsal | Proposed; no copy/rehearsal performed |
| App-signing certificate SHA-256 | null | No approved production identity supplied |
| Upload certificate SHA-256 | null | Not used for the proposed direct-APK route |
| Local-data transition | Separate install; no automatic local migration | Explicit limitation |

`Pending` is not consent. Even changing decision fields to `Confirmed` records a human decision only; it does not verify ownership, availability, account enrollment, key custody, a certificate's provenance, native acceptance or production readiness. This plan remains Stage=PlanningOnly and ReleaseReady=false. Do not place a keystore, password, recovery code, private key or secret file path in this JSON.

If Google Play internal testing is selected instead, record Distribution=GooglePlayInternal and SigningModel=PlayAppSigningSeparateUploadKey. The owner must then decide the Play App Signing setup and keep **app-signing** and **upload** certificate roles separate. Upload signing authenticates submissions; the certificate on an installed Play-delivered APK can be the Play app-signing certificate instead. A fingerprint from one role must not be accepted as proof of the other. Planning this branch does not enroll an account, pay a fee, upload an AAB or transfer any key to Google.

For that Play branch, external encrypted storage and the two owner-controlled backup copies apply to keys the owner actually holds, especially the upload key. A Google-generated app-signing private key is managed by Google and is not promised as an exportable owner backup. Account access/recovery and Play's app-signing-key procedures require separate evidence.

Direct APK testing also does not bypass Android's installation/developer-verification rules. Recheck the selected route, region, device and current verification requirements before actual distribution; no enrollment or payment is performed here.

## Custody, backup and recovery procedure to agree

1. Name the controlling owner and who may authorize signing, backup recovery, emergency revocation and distribution. For a personal project, the proposed custodian is the project owner; an organization should assign a real accountable owner before any production key operation.
2. Use dedicated app-signing material for this permanent product. Never promote the SDK debug keystore or its known development credentials to production. Existing Build-Android.ps1 remains development-signing-only, including its Release configuration; it is not a production-signing helper.
3. Select an encrypted key store/password manager or equivalent owner-controlled secure location outside the checkout, build outputs and shared chat. Keep password recovery separate from the encrypted key file. Grant access only to the signing operator and agreed recovery custodian. This phase implements no vault integration or password entry.
4. Keep at least two independent encrypted backup copies so one PC/storage/account failure does not destroy both. Document the locations/access instructions in the owner's private recovery record, not this repository. Do not put private keys or passwords into `.tools/` just because it is ignored.
5. Rehearse recovery in an isolated owner-controlled environment before relying on it. Verify the recovered key's **public certificate fingerprint** matches the approved reference; verify a disposable signed artifact and compatible update path. Retain redacted results and access review evidence, without exposing private material.
6. Prepare loss/compromise handling before distribution. Stop signing/distributing if the private key or password may be compromised, identify affected artifacts/certificates and follow the selected channel's supported key-reset/upgrade procedure. Do not assume every old Android version or distribution route accepts arbitrary key replacement. Keep an incident/recovery contact and revalidate backup access after an ownership change.

For a Play route, recovery of an upload key and upgrade/recovery of an app-signing key are separate operations under the selected Play setup. For a directly installed APK, compatible app identity/signing is part of future update continuity. This document does not implement key rotation/signing lineage, choose a certificate lifetime/algorithm or guarantee an unsupported recovery path.

## Versioning and upgrade contract

The first new permanent package starts at 1/1.0.0 in this proposal. After an actual first distribution, increment versionCode for each update on that channel and keep an auditable version/artifact ledger; a display-version change alone is insufficient. Do not reuse a versionCode for different distributed contents. Review channel/platform limits before applying the scheme; this seed validator is not a future release-number allocator.

Preserve the chosen permanent package for subsequent updates and use its compatible approved signing identity/lineage. A different package is a new app; changing only its label is not a data migration. A same-package/different-certificate install failure must be investigated instead of “fixed” by uninstalling the only copy of local data.

Before a separate permanent app is tried, the old development account workspace may upload its own pending operations through normal verified sync, then the new app can sign in and download already uploaded account tasks. Device-only tasks and an unsent outbox are not recovered by signing into a fresh app. There is no bulk export/import or cross-package key/file copying feature here. Do not copy ciphertext/credentials between packages or claim task/receipt migration without a designed, tested path. Review old-app reminder/geofence opt-ins explicitly during dual-app testing so independent installations do not unexpectedly duplicate delivery.

## Implementation inventory for a later identity increment

The validator reports source files containing the **current** development package in `.tools/android-audit/identity-plan-report.json`. The reviewed change set must include at least:

- MAUI ApplicationId/version metadata; visible label and C# RootNamespace are separate decisions.
- Fixed native capture/widget/geofence component declarations and capture action; Java component names can differ from application ID, so choose the intended mapping rather than blindly replacing every package-like string.
- Launcher shortcut targetPackage/targetClass/action and explicit PendingIntent/component behavior.
- APK filename/path assumptions, package/component/resource audit baseline, signature/upgrade role and fixture expectations.
- Authority names generated from the new app ID, SecureStorage/preferences isolation, account settings, native permissions and notification/geofence identity behavior in actual compiled artifacts.

The file inventory is a literal-reference aid, not a complete dependency graph. Platform action strings, library package names, backend account ownership and provider registrations must not be renamed indiscriminately. No automatic source rewrite is implemented. A later approved change needs a deliberate development/permanent profile split or other reviewed mapping, fresh package audits and device/data-retention evidence.

## Validate this plan

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/AndroidReleasePlan.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-AndroidReleasePlan.ps1
```

The checker reads the public plan and source inventory only. It verifies the active app ID still matches the development planning baseline, rejects unknown/secret-field shapes, placeholder/invalid proposed IDs, bad version seeds, confused signing roles, inadequate backup policy and manufactured release/recovery/native completion claims. If a current hash-matching Debug audit is available, it also rejects that known development certificate in either approved production role. Missing development-certificate evidence remains an open gate, not proof of a safe signer.

The output is **structural validation**, not approval or signing. It writes an ignored plan-hash/module-hash report with open decisions and reference files; it never invokes keytool, changes the app, reads a private keystore, signs a package or calls a console/provider. Validation failure writes a failed report when the output path is writable. This schema still covers production planning only; the selected-evaluation guard does not turn it into a production-signing workflow.

## Evidence still needed before production actions

Record actual user decisions for package, channel and custody; confirm namespace/store availability; approve the signing model, private recovery controls and certificate role; implement/review the identity mapping; then complete native Release runtime/upgrade/offline/recovery/TLS/accessibility/provider and distribution requirements. A passing plan checker or development-signed Release APK does not close these gates. The current evaluation builds continue to use the development ID and SDK key.

This plan follows Android's [application-ID guidance](https://developer.android.com/build/configure-app-module), [app-signing/key-role guidance](https://developer.android.com/studio/publish/app-signing) and current [developer-verification entry point](https://developer.android.com/developer-verification). Recheck channel-specific steps before performing future account, key or distribution operations.
