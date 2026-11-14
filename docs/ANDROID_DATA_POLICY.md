# Phase 16B: Android backup and transport policy

Implemented 2026-11-15 (Asia/Manila). **Configuration implemented; device/recovery acceptance pending. Release readiness remains blocked.**

## Data retention decision

This development app keeps its Android data local instead of promising automatic recovery of SQLite files, account outboxes, protected credentials and encrypted journals together. The manifest now sets allowBackup=false. Legacy full-backup rules exclude each available app-data domain; API 24+ adds device-protected domains. API 31+ extraction rules separately exclude every configured domain from cloud backup and Android device-to-device transfer.

**Device-only tasks and unsynced changes have no automatic backup/restore path.** Uninstalling or clearing app data can lose them. Upgrading the installed app with the same application ID and compatible signing identity preserves its existing app data; these configuration edits do not open, delete, move or reset a database or key. Previously uploaded tasks remain on the API server and can be downloaded through the normal verified account workspace. That is account synchronization, not recovery of device-only tasks or an unsent outbox. No export/import feature is added here.

The rules cover file/root/database/sharedpref/external and, where supported, device_file/device_root/device_database/device_sharedpref, with path `.` for the complete domain. This includes MAUI preferences/SecureStorage ciphertext, location encryption-key envelopes, API/Google tokens, SQLite task/cache/receipt stores and local reminder/focus/calendar/location/background state within app storage. App-specific external files are included; user-owned exports outside those domains, server data and Google events are outside this policy. Cache/no-backup handling still belongs to Android. No custom backup agent or selective task restore is introduced.

The policy requests exclusion from Android's managed paths; it does not certify every OEM, privileged migration utility or newer cross-platform transfer mechanism. Existing cloud backups or copies held elsewhere are not erased. The current SDK/target is API 36; API 36.1 cross-platform transfer matching/Apple identities are not implemented or accepted. Test the selected OS/transport before making a privacy or restore guarantee.

## Versioned configuration

| Android API | Configuration and intended behavior | Remaining limit |
| --- | --- | --- |
| 21-22 | allowBackup=false; no custom backup agent. Application endpoint validation already requires HTTPS. | Modern Auto Backup/network-security XML is unavailable; system/user certificate behavior follows these older OS versions. |
| 23 | allowBackup=false plus base full-backup exclusions for five domains; usesCleartextTraffic=false. | Cleartext flag support is platform/stack dependent; it is not a raw-socket firewall. |
| 24-30 | Legacy xml-v24 backup exclusions include all nine domains; explicit network-security configuration disables cleartext and trusts system CAs only. | Verify actual transport/certificate behavior on a device. |
| 31-36 | allowBackup=false plus separate cloud/device-transfer exclusion sets; the same explicit network-security policy. | OEM device-transfer behavior, previously retained backups, upgrade/reinstall and lost-key outcomes still require acceptance. |

Resources are Android-only: xml/backup_rules.xml, xml-v24/backup_rules.xml, xml/data_extraction_rules.xml and xml/network_security_config.xml. The manifest references them explicitly. No task/schema/server migration/protocol/permission/package/SDK change is needed. Rules apply to debug builds too; there are no permissive development-only overrides.

## HTTPS and certificate behavior

The manifest now explicitly denies cleartext traffic. On API 24+, the base network policy has cleartextTrafficPermitted=false and exactly the system certificate source. It contains no domain exceptions, user-CA trust, custom CA bundle, debug-overrides or certificate-validation bypass. ClientSession still accepts only an HTTPS API origin, with no credentials/path/query/fragment. No TLS pinning or minimum TLS version is added; OS and HTTP handler behavior still apply.

PC `localhost` and a PC-trusted development certificate do not provide a phone with a valid, reachable HTTPS endpoint. A self-signed certificate or a CA installed only in the user's certificate store is not made trusted by this policy on API 24+. Use a reachable test endpoint with an appropriate device-trusted certificate and hostname. Do not turn off validation or enable cleartext merely to make phone testing pass. No endpoint, certificate installation, tunnel, provider request or deployment is performed by this increment.

## Automated checks

Run the same Phase 16A commands:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/AndroidPackageAudit.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Android.ps1
```

The audit now requires backup/cleartext manifest flags to be false and verifies their resource-ID links. It inspects actual packaged backup/network XML variants, complete exclusion sets, no includes/narrowed paths, mandatory separate extraction modes, cloud encryption condition and system-only trust with no domain/debug overrides. Missing/expanded rules fail instead of being silently accepted. No custom backup agent is allowed without a separate reviewed policy. The report records configured policy separately from acceptance; ReleaseReady remains false. See ANDROID_RELEASE_READINESS.md for scope, hashes and signing limits.

PowerShell tests mutate valid policy documents to exercise missing credential/task/device-domain exclusions, missing transfer mode, includes/narrowed paths, encryption weakening, cleartext, user certificates and debug/domain overrides. Existing automated SQLite/crypto/session/outbox tests support the underlying fail-closed recovery behavior; they do not execute Android backup or TLS on a phone.

## Device and recovery evidence still required

Use disposable test data and an isolated test installation. Follow the evidence format in ANDROID_RELEASE_READINESS.md; do not perform these experiments on the only copy of device-only tasks or queued changes.

1. Upgrade from the previous APK with the same ID/signing identity. Verify device task IDs/fields, unsent account operations and capture receipts survive, protected sessions retain their normal verification rules and reminders do not gain a new opt-in.
2. Exercise available cloud and Android device-transfer test transports on selected old/new/OEM devices. Confirm excluded files/preferences are not transported. Reinstall/clear-data experiments belong only to the disposable installation; check fresh local state and no restored credentials, copied encrypted journal or automatic geofence rearm. Policy XML alone is not acceptance evidence.
3. Test an old backup/foreign encrypted file and a missing/corrupt location key through the documented fail-closed paths. Never manufacture/copy another account's key or delete an unreadable store as a repair. Use explicit Stop/block controls and retain redacted diagnostic evidence.
4. Queue a capture while offline, reopen and sync with the same account, then verify once-only upload. Separately prove that a fresh installation does not recover an unsent queue through server sync. After an uncertain Save, inspect the workspace before another capture; a new screen has a new capture ID.
5. On API 24+, try a reachable valid HTTPS endpoint, wrong hostname/expired/untrusted certificate and HTTP. Valid TLS should work; invalid TLS/HTTP must fail without a token leak or persistence reset. Repeat applicable API 21-23 cases and document older platform trust/flag limitations instead of assuming newer XML semantics.
6. Complete the capture/geofence/accessibility/provider/backup matrices and approved release signing independently. These edits do not approve release, prove Play review or close Apple/Windows acceptance.

Android's [backup rules](https://developer.android.com/identity/data/autobackup) distinguish legacy, cloud and device-transfer configuration. [MAUI SecureStorage guidance](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0) explains why restored encrypted preferences can be unreadable without their original device keys. The explicit trust policy follows [Android network security configuration](https://developer.android.com/privacy-and-security/security-config).
