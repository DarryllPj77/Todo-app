# Google Calendar consent - Phase 13B desktop

This increment implements Windows system-browser consent, PKCE/state, protected account-bound token storage, manual connection checking/refresh, cancellation, local disconnect and explicit Google revocation. This connection screen itself does **not** create, list, update, delete or synchronize calendar events. Phase 13C now supplies separate explicit publishing through Tools > Google event links; see [GOOGLE_EVENT_LINKS.md](GOOGLE_EVENT_LINKS.md). Local planning/ICS export remains in CALENDAR.md. Actual Google consent, native SecureStorage and browser/socket acceptance remain required.

## Configure and try it

1. In your Google Cloud project, enable **Google Calendar API**. Configure the OAuth consent screen/branding, audience and test users as applicable to the project's current publishing mode. Use a project dedicated to this learning app; do not commit credentials or account tokens.
2. Create an OAuth client with application type **Desktop app**. Copy its client ID and, if required by that client, its client secret. This client secret is optional in Google's protocol and cannot be treated as confidential in a distributed desktop app. It is still stored only in protected credentials here. See [Google's desktop OAuth setup](https://developers.google.com/identity/protocols/oauth2/native-app).
3. Rebuild the Windows client with `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Windows.ps1`. No API/database migration or new NuGet package is needed.
4. Use **Tools > Account & server** to sign in to the Todo API and verify the session. A restored but unverified or expired Todo session cannot start Google authorization. Google identity is separate from the Todo JWT/account.
5. Open **Tools > Google Calendar connection**, enter the Desktop client ID and optional secret, and choose **Connect in browser**. Enter Google credentials only in the system browser. Choose the intended Google account and review the requested permissions there.
6. The client listens on an OS-selected IPv4 loopback port before launching the browser. Google returns to `http://127.0.0.1:<port>/oauth2/callback`. Return to the app to see whether identity validation and protected storage succeeded. A browser message only confirms receipt of a response, not a successful connection.
7. **Check saved connection / refresh token** reads the active Todo account's protected grant, refreshes if within one minute of expiry, and verifies Google identity through UserInfo. It never opens the browser automatically. Client secret entry text is cleared when an action completes or the Todo account changes.

The desktop flow is disabled for Android/iOS/Mac Catalyst in this increment. Google's desktop loopback flow must not be reused for mobile client types; supported native mobile authorization is still pending. The isolated Debug layout preview also disables Google authorization and substitutes memory storage/offline HTTP.

## Permissions and endpoints

Requested scopes are `openid`, `email`, `https://www.googleapis.com/auth/calendar.calendarlist.readonly`, and `https://www.googleapis.com/auth/calendar.events.owned`. These support identity plus the upcoming explicit calendar/event-link increment. The UI describes those permissions before launching consent. Owned-event access permits managing events on calendars the user owns; it is narrower than the all-calendar events scope. See [Google Calendar scopes](https://developers.google.com/identity/protocols/oauth2/scopes#calendar). The code currently calls only these fixed endpoints:

```text
accounts.google.com/o/oauth2/v2/auth       system-browser consent
oauth2.googleapis.com/token               code exchange / refresh
openidconnect.googleapis.com/v1/userinfo  authenticated subject/email verification
oauth2.googleapis.com/revoke              explicit revocation
```

Google HTTP has a separate client, no automatic redirects, a 15-second timeout and a 64-KiB response buffer limit. Todo bearer tokens are never attached to Google requests. Provider response bodies, authorization codes, access/refresh tokens and client secrets are not bound to labels or included in application error messages. The grant's ToString is redacted. ID tokens are not used as unvalidated identity: subject/email come from Google's authenticated HTTPS UserInfo endpoint using the access token issued by the PKCE code exchange.

## Lifecycle and security policy

- Each consent attempt has fresh 256-bit random state and a fresh 43-character verifier with SHA-256 challenge. The code exchange includes that verifier and the exact loopback redirect. Offline access and explicit account selection/consent are requested. Missing required scopes, a refresh token, usable Bearer token, or bounded positive expiry rejects the connection.
- The listener binds only `127.0.0.1`, validates the exact callback path and Host header, rejects duplicate query fields, and ignores unrelated/wrong-state traffic. It accepts bounded HTTP/1.1 GET headers, gives each connection five seconds and the whole browser flow three minutes, returns non-echoing no-store text, and releases its port on cancellation. No HttpListener URL reservation or LAN listener is used.
- A Google grant is bound to the Todo API-origin/account partition, Desktop client ID and verified stable Google subject. A changed Google subject is rejected; an email change for the same subject is allowed after verification. A second Google account/client requires explicit disconnect first.
- SecureStorage holds the version-1 JSON grant, including optional client secret, Google subject/email, scopes, tokens and expiry. Scope-hashed key names separate accounts/origins. No token or credential goes into SQLite, source files, Preferences, logs or the Todo API.
- Preferences stores only a non-secret restoration block marker. Writes persist the block before updating SecureStorage and unblock only after success. Disconnect persists a block before attempting secure deletion. Failed removal cannot revive the old grant. If the marker itself fails, credentials remain blocked in the running process and the app reports that restart protection could not be confirmed; remove Google access externally and retry local disconnect.
- A singleton gate serializes windows' operations. The original Todo scope/version is captured **before** waiting for the gate. Account switches/logout cancel active work; old/queued callbacks, token results and protected writes are checked before being accepted. A result rejected after protected storage is blocked rather than restored later. A token request already sent may have granted/revoked access at Google even if cancellation prevents the app from observing the outcome.
- Todo logout normally retains already saved, scope-bound Google credentials for that Todo account; it clears visible connection data and cancels work. It does not revoke Google permission. Expired/unverified Todo sessions cannot load/use Google credentials. UserInfo/invalid_grant rejection blocks local restoration; network/transient failures retain credentials for retry.
- Refresh rotation is saved when provided. If Google omits a replacement refresh token, the prior one is retained. Subject verification and secure persistence occur before displaying success. Connection checks are explicit, not a background scheduler.
- Cancel and navigating away from the connection page cancel its in-flight action. The browser tab is not closed automatically. If consent was granted but saving failed/cancellation won the race, remove access in your Google Account if desired before retrying.

## Disconnect and revoke

**Disconnect locally** asks for confirmation, blocks/removes this Todo account's local Google credentials, and makes no Google request. It leaves Google permission intact. **Revoke Google access and disconnect** separately asks for confirmation, posts the refresh token in the HTTPS form body, and removes local credentials after a successful revocation response. Failed/unconfirmed revocation retains credentials for retry; local disconnect is still available.

Google documents that revocation affects a project's granted scopes across its clients, not just one calendar link. The confirmation describes that scope. Existing Google calendar events, imported ICS copies and local time blocks are not deleted by either action. If no usable credentials remain, remove the app from your [Google Account third-party connections](https://myaccount.google.com/connections). See [Google's revocation policy](https://developers.google.com/identity/protocols/oauth2/native-app#tokenrevoke).

## Native acceptance still required

- [ ] Configured Google test project/client: consent accepted/denied, partial grants, offline refresh, cancellation, closing browser, timeout, retry, no secret error output.
- [ ] Windows browser launch/loopback/firewall behavior, actual SecureStorage restart, protected-storage failure/recovery, and multi-window behavior.
- [ ] Todo logout/switch/expiry while queued, in browser, exchanging, identifying, persisting and confirming revoke/disconnect; old identity must not appear under another Todo account.
- [ ] Manual Google-side revocation followed by Check; refresh-token expiry/rotation; uncertain revocation/network loss and local-forget recovery.
- [ ] Narrow/keyboard/light/dark/large-text/screen-reader UI and account labels. No native browser/share/account operation was performed by automated desktop control in this increment.
- [ ] Mobile platform-specific supported authorization and toolchain/device acceptance. Existing Phase 13A and earlier native gates remain open.

**13C explicit event links are now implemented separately** with selected calendars, durable provider IDs, pending-operation recovery, conditional edits and confirmed remote deletion; see [GOOGLE_EVENT_LINKS.md](GOOGLE_EVENT_LINKS.md). Actual provider/native acceptance and Phase 13D two-way conflict/reconciliation remain open. Consent alone does not synchronize events.

## Verification recorded on 2026-11-14

All 457 tests passed, 0 skipped (398 portable cases, including 26 new OAuth cases, and 59 PostgreSQL facts). The final isolated Windows solution/XAML build passed with 0 warnings/errors. Tests use mock Google HTTP and fake protected storage, plus actual IPv4 loopback requests for bad-state rejection, valid callback and listener cancellation/rebinding. They cover PKCE's published vector, duplicate/denied callbacks, missing/unverified Todo sessions, partial scopes, refresh preservation/rotation, invalid grant, subject mismatch, stale/queued account changes, secure write/block failure, local versus provider disconnect, and confirmation/logout races.

No live Google credentials, authorization, UserInfo, refresh or revocation were used. Native browser/SecureStorage/UI, configured provider acceptance and mobile flows remain unverified. The repository PostgreSQL cluster was cleaned/stopped; the installed PostgreSQL service remains running.
