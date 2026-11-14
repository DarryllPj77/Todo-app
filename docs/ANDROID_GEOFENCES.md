# Phase 14B.3: Android background geofences

Android now has an opt-in background adapter for the existing **one device reminder / thirty-minute run**. It uses Google Play services, the encrypted run journal and the key-independent Stop generation introduced in [BACKGROUND_GEOFENCES.md](BACKGROUND_GEOFENCES.md). A native Android APK can be built, but real permission, geofence and delivery behavior still requires a phone or suitable emulator. No device was installed, launched or queried during implementation. iOS/Mac background adapters remain unavailable.

## Build and try

The application targets Android API 36 with its existing minimum API 21. Background geofencing is gated to API 23+ with working Google Play services; older/non-Play builds retain manual task/reminder features. A Google Play-enabled device/image is required for this adapter. There is no Calendar OAuth/client-ID setup for location geofencing.

Use a .NET 10 SDK with the `maui-android` workload. This checkout uses the isolated `.tools/dotnet` SDK. The first native build installed its Android workload, Android SDK/build tools and Microsoft OpenJDK under ignored `.tools` output. No emulator/system image or global Java/Android environment variable was configured.

```powershell
# First-time SDK/JDK setup: explicitly downloads dependencies and accepts their development licenses.
.\scripts\Build-Android.ps1 -InstallDependencies

# Subsequent standalone debug APK builds.
.\scripts\Build-Android.ps1
```

The workload must already be installed in the SDK selected by the script. The script uses workspace-local SDK/JDK/download cache paths and builds a debug-signed APK with embedded assemblies. It does not install on a device, launch an emulator, request app permissions or use release signing credentials. See [Microsoft's Android dependency installation guidance](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies).

Current artifact path:

```text
.tools/android-build/bin/TodoApp.Maui/debug_net10.0-android/com.companyname.todoapp.maui-Signed.apk
```

Copy/install that **Signed.apk** through your normal Android development/installer workflow. It is a debug test build, not a store release. Open the app, create a disposable pending Device task, then open Tools > Location reminders, save/arm a place and select it. Allow notifications before Start. No running Todo API is needed for this device-local workflow.

## Permission flow

Start shows a contextual explanation before native access. Manual checks remain usable when background access is declined. No permission is requested at boot, from a receiver, on Activity resume or merely by opening the page.

- Enable device location services and allow notifications. Android 13+ notification permission is requested only through the existing Allow/check notifications button.
- Allow precise foreground location. Approximate/coarse access alone cannot enable this background adapter, even when the generic permission result says Granted.
- Android 10/API 29 requests background location separately, after foreground access.
- Android 11/API 30+ requires background access in app settings. Use the explicit **Open Android location permission settings** button, enable precise location and **Allow all the time**, return and press Start again. The button asks for confirmation and never automatically starts a run. Permission dialogs/settings can cancel a pending page action; grant access and explicitly retry.

The version-specific distinction and approximate-location behavior follow [Android background permission guidance](https://developer.android.com/develop/sensors-and-location/location/permissions/background). Settings labels/screens vary by Android/OEM/language. Missing/disabled/outdated Play services report unavailability without launching a resolver or breaking manual task features.

## Native run and callbacks

One run creates two regions with opaque run IDs: an inner half-radius dwell region and an outer accuracy-margin exit region, sharing the conservative geometry from Windows. Entry requires ten seconds of native dwell. The requested notification responsiveness is sixty seconds, not a delivery promise. Initial native entry is disabled; a fresh reliable outside observation is still required before arrival. Both regions expire by the original thirty-minute UTC authorization deadline. Radius 200 m or larger is a useful starting point for testing because the inner region is smaller than the saved radius.

Play services receives the saved center/radii outside the application's AES-GCM envelope. It sends transitions through an explicit, nonexported receiver PendingIntent. Android 12+ uses a mutable PendingIntent as required for geofence extras; the component/action are explicit, and this feature has its own request identity. Cleanup removes only geofences associated with that exact PendingIntent. The native Add acknowledgement is awaited under the shared lease so a canceled managed wait cannot release ownership before a late Add finishes.

AndroidGeofencePolicy accepts only bounded dwell/exit reports with correctly shaped opaque identities. Initial enter, unsupported transitions, missing locations, oversized/foreign IDs and wrong suffixes are ignored. Exact current run/arm matching, task/occurrence validation, mocked/stale/inaccurate fix checks and one-attempt consumption remain enforced by BackgroundGeofenceService. No new location fix is requested from a receiver.

Receivers use GoAsync with a bounded wait and complete their pending result. They launch no Activity or foreground service and request no permissions. Android necessarily initializes the application's process/Application infrastructure; the shared device worker graph itself creates no API client, account restoration, foreground timer or new location provider. The same graph is now used by the Windows headless host. No position sample, travel history or account-token access is added.

Android may delay background geofencing by minutes, especially with Doze, poor accuracy, disabled Wi-Fi/location services, OEM battery restrictions or stale provider fixes. Shared freshness/accuracy checks can intentionally miss delayed/unreliable arrivals. Force-stop can prevent callbacks until the user opens the app; an APK build cannot validate those behaviors. See [Android geofencing guidance](https://developer.android.com/develop/sensors-and-location/location/geofencing).

## Restart, resume and opt-out

This adapter deliberately does **not** re-register saved regions after reboot, app update, data loss or force-stop. BOOT_COMPLETED and MY_PACKAGE_REPLACED block the saved generation and attempt cleanup. The existing time-reminder boot restoration is separate and unchanged.

A newly created foreground MainActivity also invalidates a saved background run. This conservative policy handles uncertain force-stop/process/Activity restoration without pretending to distinguish their causes. Returning to an existing Activity with valid permissions can preserve its run; a cold UI launch requires a fresh explicit Start. Android process creation for a geofence receiver does not itself revoke the run, so ordinary OS process death does not automatically prevent an otherwise authorized callback.

The durable block is written before asynchronous lifecycle cleanup. Cleanup checks the captured generation under the shared lease; a delayed boot/update/cold-launch callback cannot stop a later run the user explicitly authorized. Resume checks permissions/services without prompting or sampling location. Revocation/unavailability blocks delivery and attempts cleanup. Native errors can stop the current run; there is no silent re-registration.

Stop background run remains available independently of workspace/account selection and encrypted-key readability. It blocks first, then attempts journal/native cleanup; retry if cleanup fails. Failed removal can leave bounded OS geometry until expiration, but blocked/stopped authorization cannot consume or notify. SQLite, Play services and notification delivery are not one transaction; existing best-effort cancellation/crash limits still apply.

## Dependencies and verification limits

Verification (2026-11-14): **30 new portable cases**, **142 targeted location cases** and **671 full-suite cases** pass with zero skips (612 portable plus 59 PostgreSQL). Final Android APK and Windows solution builds have zero warnings/errors. APK v1/v2/v3 signatures, one signer and embedded .NET 10 ELF-wrapped app/domain/storage assemblies were confirmed. Actual APK manifest contents confirm background permission, receiver export boundaries, boot/update filters and target API 36. The Windows MSIX rebuild retains its two known PRI resource warnings. These are build/artifact checks, not device execution.

The Android-only binding is pinned to [Xamarin.GooglePlayServices.Location 121.3.0.7](https://www.nuget.org/packages/Xamarin.GooglePlayServices.Location/121.3.0.7), exposing native Play services Location 21.3.0. A trial with 121.4.0 pulled AndroidX versions outside the pinned MAUI graph's constraints; the compatible binding restored without those warnings. Do not update binding/transitive AndroidX versions independently without rebuilding. No new package is added to Windows/core targets.

Portable tests cover permission staging, report mapping, lifecycle generation races, shared worker device-only reads and the existing run/crypto/one-attempt behavior. Native builds additionally surfaced older Android notification nullability/API guard warnings; those guards were corrected without suppressing analyzer rules. Android manifest inspection checks background permission, receiver export boundaries, boot/update filters and Play services metadata. SDK debug signing and APK contents are verified separately from real execution.

Before release, test API 23-28/29/30+/33+ permission flows, approximate/precise denial, settings return, missing Play services, actual dwell/exit, app closure/process death/force-stop/cold reopening, reboot/update, revoked permissions, Doze/OEM restrictions, lost-key/backup restoration, multiple callbacks/Stop races, notification channels/taps, native layout and accessibility. Apple support and earlier Windows/provider acceptance remain open.

Google Play distribution needs its background-location justification, disclosure/privacy and review work; this debug implementation is not Play approval. See [Google Play's background-location requirements](https://support.google.com/googleplay/android-developer/answer/9799150). No store submission, release signing, device location or permission operation occurred during this implementation.
