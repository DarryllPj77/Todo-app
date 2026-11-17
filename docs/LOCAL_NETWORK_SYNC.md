# Physical-phone and Windows synchronization setup

Last checked: 2026-11-17 (Asia/Manila). **Setup incomplete; no two-device synchronization pass claimed.**

## Current evidence

| Check | Result |
| --- | --- |
| API executable starts | The existing Debug binary executed and reached Program.cs configuration validation; the previous application-control denial did not occur. |
| API listening/health | Blocked: ConnectionStrings:TodoDatabase is missing. Jwt:SigningKey is also absent from API user-secrets and the checked process environment. |
| PostgreSQL service | postgresql-x64-18 running; pg_isready reports 127.0.0.1:5432 accepting connections. |
| Database authentication/schema | Unverified: a password-free probe received fe_sendauth: no password supplied. This does not establish whether the proposed todoapp database/role exists. |
| Windows HTTPS certificate | Existing trusted, unexpired development certificate for localhost was found using a check-only command. No certificate was created/imported/trusted by this session. |
| Windows client to API | Pending API startup and actual client authentication. A trusted certificate alone is insufficient evidence. |
| Another LAN device to API | Pending listener, certificate, network and phone verification. Current HTTPS launch profile listens on localhost only. |
| Same account and task CRUD convergence | Pending on both actual clients; device-only acceptance is separate. |

Observed PC Ethernet address: 192.168.254.177/24. Recheck before use; DHCP can change it. The PC need not use Wi-Fi if its Ethernet network and the phone's Wi-Fi permit communication. Guest Wi-Fi/client isolation may prevent that. No router/network/firewall/security setting was changed.

## Configure existing local database credentials privately

The API already uses PostgreSQL/EF Core, JWT authentication and per-account sync partitions. Preserve that architecture. There is no in-memory fallback or automatic schema migration.

The user confirmed a new Todo database is needed. Prepared, but **not executed**, is a bootstrap script for new names todoapp_phone_sync and todoapp_sync_dev. In your own interactive PowerShell terminal, use the installed administrator login (postgres below is the usual name):

```powershell
& 'E:\PostgreSQL\18\bin\psql.exe' -X -h 127.0.0.1 -p 5432 -U postgres -d postgres -W -f .\scripts\Initialize-DevelopmentDatabase.sql
```

Enter the PostgreSQL administrator password at the first hidden prompt. The script then asks twice for a NEW password for todoapp_sync_dev. Keep that new password privately for the API helper. It creates only the dedicated role/database, uses SCRAM-SHA-256 for the new password, denies default PUBLIC database access and grants no server-administration/role-creation/database-creation privileges to the app role. No PostgreSQL authentication/listener rules change. Existing names cause a stop before creation; no existing database or role is reused/dropped/modified. A later failure can leave newly created objects (the role begins NOLOGIN); review them rather than blindly deleting/retrying. Database creation cannot be wrapped in the same transaction as role creation.

If you know an existing database name, role and password, run interactively from the repository root:

```powershell
& .\scripts\Configure-DevelopmentApi.ps1
```

The helper asks for the database/role/port and a hidden password, sets only missing ConnectionStrings:TodoDatabase and Jwt:SigningKey through the existing API UserSecretsId, and preserves existing nonblank values. It generates a random 32-byte JWT key only when absent. Password-containing JSON goes to dotnet through standard input instead of command-line arguments. Development user-secrets are outside Git but **not encrypted**; use development-only credentials. The helper is not a database/account provisioner, credential verifier, security-policy change or migration command. Do not paste passwords, tokens or secrets.json into chat. If credentials/database are unknown, use the local PostgreSQL administration workflow; do not change pg_hba.conf to trust or reset an unknown database.

For the new database, enter todoapp_phone_sync, todoapp_sync_dev, port 5432 and the newly chosen app-role password. Do not configure the API to use the postgres administrator login.

Once credentials are configured, verify database authentication and inspect all eight migration records before applying anything. Apply pending migrations explicitly only to the intended development database using DATABASE.md, then start the HTTPS profile. Existing databases/tasks are not to be dropped or reset to make setup pass.

## HTTPS and LAN decision required before network changes

The existing certificate is for localhost, not 192.168.254.177. Phone localhost is the phone, and 10.0.2.2 is an emulator-only host alias. The Android API 24+ policy trusts system CAs only; adding a user CA alone does not establish app trust. Do not disable validation, enable HTTP or widen Android CA trust to get a connection.

For direct LAN HTTPS, agree on a hostname covered by a device-trusted certificate and resolving to this PC on both clients. A domain the owner controls with an appropriately issued certificate can supply that identity. Explain and approve the certificate provisioning/storage and DNS change first; do not commit a certificate private key. Then bind Kestrel to the chosen LAN address/HTTPS port with that certificate. A local self-signed localhost certificate is not a valid LAN substitute.

If Windows Firewall blocks that agreed listener, explain a proposed narrowly scoped inbound rule before making it: API HTTPS TCP port only, the relevant executable where practical, Private profile only, and the intended phone IP/local subnet only. Do not disable Firewall, broadly open all ports, change the network profile silently or open PostgreSQL to the LAN. A rule is not yet demonstrated necessary and none was added. A localhost probe cannot prove remote-device reachability.

An HTTPS tunnel is a different topology: it publishes the test API through an external provider and does not prove direct LAN ingress. It needs explicit approval of exposure/provider and a review of HTTPS/proxy handling; no tunnel was installed or started. It is not the default substitute for the requested LAN check.

### Approved private-network alternative (setup in progress)

The user has no domain name. Tailscale Serve can instead issue HTTPS for a ts.net device hostname and restrict access to the user's private Tailscale network. This requires a Tailscale account, installation/sign-in on PC and phone (adding a network service/virtual adapter/VPN connection and provider-managed network configuration), enabling tailnet DNS/HTTPS and obtaining a publicly trusted certificate. The issued hostname appears in public certificate-transparency logs. Review the connected devices/access policy; the Todo account/JWT checks remain mandatory. This changes network topology and does not certify direct Ethernet/Wi-Fi LAN ingress. Do not use Funnel/public sharing, exit-node/subnet-router features or unrestricted firewall changes.

The user approved this private setup on 2026-11-17. Tailscale 1.104.7 is now installed from the official signed MSI. Winget verified its download hash; Authenticode validated Tailscale Inc. as publisher. The initial non-elevated MSI attempt stopped with error 1603 (elevation required); the normal UAC-elevated attempt completed with exit 0. The Tailscale service is running as LocalSystem. Installed app policies prohibit accepting subnet routes and advertising this PC as an exit node. The user completed PC sign-in; the latest status is Running with PC online, zero Android peers and empty Serve configuration. No manual firewall rule was added, no Windows security setting was toggled, and no API port was opened to the LAN. Installer/service networking is part of the approved Tailscale installation.

Trust review found the localhost development certificate in CurrentUser Root, but not LocalMachine Root. A successful user-level HTTPS request therefore would not establish that the LocalSystem proxy trusts it. Do not use https+insecure or import a new machine-wide root silently. One development approach preserving existing trust stores is to obtain the approved Tailscale hostname's publicly trusted certificate, configure Kestrel's development endpoint to present it, and use Serve raw TCP passthrough to loopback. The clients then validate that hostname directly against Kestrel's certificate; Serve does not terminate or downgrade TLS. This requires protecting the issued private-key files outside Git, checking the hostname/chain, and managing renewal. No certificate was issued, Kestrel certificate changed or TCP mapping added yet. Before adding a mapping, inspect existing Serve configuration again. Both apps must use the same resulting https://<device>.<tailnet>.ts.net/ origin.

Provider references: [Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve), [Serve command, TCP passthrough and upstream validation](https://tailscale.com/docs/reference/tailscale-cli/serve), [HTTPS and certificate-transparency disclosure](https://tailscale.com/docs/how-to/set-up-https-certificates). This is an approved development connection still being configured, not a production deployment or a passing two-device result.

Use the same canonical HTTPS origin on both clients. Their sync cache partitions include API origin and user ID, so using localhost on one and a LAN hostname on the other produces separate local partitions even when the backend is shared. Changing the address signs out; retain pending work under its original partition.

## Actual two-client acceptance (all pending)

1. Open the agreed HTTPS /api/health endpoint from the PC and phone with normal certificate validation and no browser warning bypass. Health is liveness only; verify PostgreSQL independently.
2. On each app: Tools > Account & server, apply the same canonical API address, then sign in to the same test account. Register once if necessary; enter credentials locally. Confirm account identity on each client without recording tokens/passwords.
3. Open Tools > Synced tasks on both and Sync. Device workspace tasks do not upload automatically; create disposable tasks through New synced task.
4. Phone: create a uniquely named task, Save locally & queue, then Sync. PC: Sync and confirm exactly one matching task. Record actual results and any errors.
5. PC: edit the title/description, Save locally & queue, then Sync. Phone: Sync and verify the edit.
6. Phone: select the task, check Completed, Save locally & queue, then Sync. PC: Sync and verify completion with a filter that includes completed tasks.
7. PC: select the disposable task, Queue deletion and accept the app's confirmation if shown, then Sync. Phone: Sync and verify removal. Confirm no unrelated task changed.
8. Repeat in the reverse direction where needed and confirm reopen persistence. Record observed device/app/artifact/origin and pass/fail; portable tests alone do not establish this acceptance.

No phone/PC sign-in or actual shared-backend CRUD result has been recorded for this setup session.

References: [ASP.NET Core development secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), ANDROID_DATA_POLICY.md, DATABASE.md, SYNCHRONIZATION.md and ANDROID_NATIVE_ACCEPTANCE.md. Existing SYNCHRONIZATION.md certificate instructions must be read with the stricter Android system-CA policy above.
