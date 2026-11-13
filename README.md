# Todo App

A learning-focused C#/.NET Todo application, built one phase at a time. It includes an offline MAUI task manager and a separate authenticated online workspace backed by a PostgreSQL API. Synchronization is planned next.

**Current status (2026-11-13): Phase 9 MAUI account and API integration implemented.** The client supports registration/sign-in, protected session restoration, logout, configurable HTTPS endpoints, and a separate online server-task workspace. All **163 tests passed, 0 skipped**, with PostgreSQL enabled; the Windows-targeted solution built with zero warnings/errors. Native device HTTPS and SecureStorage round trips remain manual acceptance checks. Configure the API using [DATABASE.md](docs/DATABASE.md). Synchronization is Phase 10 and is not implemented.

## Technology and prerequisites

- Installed/selected SDK: .NET **10.0.404**, pinned in `global.json`; latest patch within that feature band is allowed.
- MAUI 10, XAML, MVVM, built-in dependency injection; Microsoft.Maui.Controls **10.0.130**, a stable .NET 10 release checked against NuGet. The official matching template supplied platform files and resources.
- ASP.NET Core 10 controller API using the SDK's shared framework.
- xUnit and ASP.NET Core's in-memory integration test host.
- LINQ for local task filtering and ordering; no additional package is needed.
- Microsoft.Data.Sqlite **10.0.15** for local SQLite storage, including its native SQLite dependency.
- EF Core/Relational/Design **10.0.15**, local dotnet-ef **10.0.15**, and Npgsql.EntityFrameworkCore.PostgreSQL **10.0.3** for server PostgreSQL storage.
- Microsoft.AspNetCore.Authentication.JwtBearer **10.0.15**, ASP.NET Core's password hasher, per-IP auth rate limiting, and per-user server ownership.
- HttpClient, client DTOs, platform SecureStorage, and an online account workspace. Planned next: offline synchronization.

Core projects require the selected SDK and NuGet access. MAUI additionally needs platform workloads/tools. On Windows, use Visual Studio with .NET MAUI development support, or the .NET MAUI extension for VS Code and the required native tooling. Android needs its SDK/JDK and a device/emulator. Apple targets require a Mac with compatible Xcode; Windows cannot independently build/run every Apple target.

## Solution structure

```text
TodoApp.slnx                 All five projects, including MAUI
TodoApp.Core.slnf            Same solution, excluding MAUI for SDK-only checks
src/
  TodoApp.Domain/           Entities, Enums, Services contracts, shared query rules
  TodoApp.Infrastructure/   SQLite service; server DbContext, mappings, migrations
  TodoApp.Api/              Controllers, Contracts, Services, database Configuration
  TodoApp.Maui/             Views, ViewModels, Models, Services, Resources, Platforms
tests/TodoApp.Tests/        Domain, storage, ViewModel behavior, and REST integration tests
scripts/Run-Windows.ps1     Windows build/run launcher (PowerShell 5.1 compatible)
scripts/Test-Postgres.ps1   Real PostgreSQL tests using a dedicated test connection
dotnet-tools.json          Repository-local EF tool version
docs/                      Roadmap, architecture, database plan, learnings, progress
```

The repository folder itself is the solution root; no extra nested `TodoApp/` directory is necessary. `.gitkeep` files preserve reserved folders without inventing implementations.

## Setup and core verification

From the solution root in PowerShell:

```powershell
dotnet --info
dotnet workload list
dotnet restore TodoApp.Core.slnf
dotnet build TodoApp.Core.slnf --no-restore
dotnet test TodoApp.Core.slnf --no-build --no-restore
```

The filter does not convert MAUI into a plain .NET library or claim to validate client code.

The default test run needs no database: portable REST tests explicitly substitute memory and a test-only authentication handler. Nineteen PostgreSQL facts report skipped unless TODOAPP_TEST_POSTGRES is set; these include real JWT/login/ownership tests. The full Phase 9 run used an isolated PostgreSQL 18 cluster: **163 passed, 0 skipped** (default mode: 144 passed, 19 skipped). See DATABASE.md to run the full suite; fake authentication is never registered by the normal API.

## Run the API

First follow [database/auth setup](docs/DATABASE.md): configure ConnectionStrings:TodoDatabase and a random Base64 Jwt:SigningKey through user secrets/environment variables, restore the local EF tool, and apply both included migrations. The API has no automatic startup migration, generated fallback signing key, or silent in-memory fallback.

```powershell
dotnet dev-certs https --trust
dotnet run --project src/TodoApp.Api --launch-profile https
```

Open `https://localhost:7114/api/health`; expected JSON is `{"status":"healthy"}`. The HTTP listener is `http://localhost:5062`, redirected to HTTPS by the pipeline when the HTTPS profile is used. `src/TodoApp.Api/TodoApp.Api.http` contains the same request. Stop with Ctrl+C.

The health endpoint is public liveness only, not database readiness. Task routes and /api/auth/me require a valid bearer token. Startup requires database/JWT configuration and remains Development-only until deployment hardening/acceptance. The launch profiles set Development automatically. API tasks persist in PostgreSQL and remain separate from MAUI's device-local SQLite tasks; there is no synchronization yet.

### Register, log in, and access tasks

| Route | Behavior |
| --- | --- |
| POST /api/auth/register | Email/password; 201 with bearer token, expiry, and public account fields |
| POST /api/auth/login | Email/password; 200 with a new token, or generic 401 failure |
| GET /api/auth/me | Authenticated account ID/email; 401 without a valid token |

Register passwords must be 12–128 characters; passwords are never trimmed or stored as plaintext. Email is normalized for uniqueness; duplicate registration returns 409. Auth requests reject unknown fields. Register/login share a process-local limit of 10 requests per IP per minute, returning 429 when exceeded. Use the HTTPS launch profile for credentials/tokens.

Copy accessToken from a successful response into your HTTP client's private environment and send `Authorization: Bearer <accessToken>` on task/me requests. The included .http file contains placeholders only; never commit actual passwords/tokens. Tokens default to 15 minutes, validate signature/issuer/audience/lifetime with no clock-skew grace, and are rejected for a missing account. There is no refresh token; log in again after expiry. Client logout will clear its stored token; already issued tokens remain valid until expiry (or signing-key/account invalidation).

Every task operation scopes by the token's user ID. Another user's ID returns 404 for reads/updates/deletes; callers cannot submit ownerId/categoryId to change ownership. Category names are reused within each user, so two users may both have Work. Existing anonymous Phase 7 rows remain stored with null OwnerId and are hidden until explicitly assigned by an administrator; registration never claims them automatically.

| Method and route | Success | Other expected responses |
| --- | --- | --- |
| GET /api/tasks | 200 with an array (possibly empty) | 400 for an invalid filter |
| GET /api/tasks/{id} | 200 with a task | 404 if missing or the ID route is malformed |
| POST /api/tasks | 201 with a task and Location header | 400 for invalid JSON/input |
| PUT /api/tasks/{id} | 200 with the updated task | 400 for invalid input; 404 if missing |
| DELETE /api/tasks/{id} | 204, no response body | 404 if missing |

All five task routes additionally return 401 for missing/invalid credentials.

Use `?filter=All`, `Pending`, `Completed`, `DueToday`, or `HighPriority`. DueToday uses the server's local calendar day for this development phase, not a phone's time zone. DueToday/HighPriority include completed tasks when they match. Tasks sort by title (ordinal, case-insensitive), then ID.

POST/PUT accept only these fields:

```json
{
  "title": "Learn REST",
  "description": "Try the task endpoints",
  "dueDate": "2026-11-20",
  "priority": "High",
  "categoryName": "Learning",
  "isCompleted": false
}
```

Priority is a JSON string: Low, Normal, or High; numeric values are rejected. DueDate is a nullable ISO calendar date. PUT is a full replacement: omitted description/dueDate/categoryName become null, priority defaults to Normal, and isCompleted defaults to false (reopens a completed task). Include every editable field whose value you want to retain. POST may also create an already completed task by explicitly setting isCompleted.

IDs/timestamps are server-generated response fields; categoryId is read-only. Unknown JSON properties (including id/createdAt/updatedAt/categoryId/navigation objects) return 400 instead of being silently accepted. Titles, descriptions, and category names use the existing trimmed domain length rules. Existing category names are reused case-insensitively; category management is not exposed yet. Responses are flat DTOs with categoryId/categoryName and no entity navigation graph. Validation errors return Problem Details with field errors; missing tasks return 404 Problem Details.

Open src/TodoApp.Api/TodoApp.Api.http in an HTTP client to try register/login and the task workflow; keep actual credentials/token variables in private client settings and copy the created task ID into taskId. Prefer HTTPS. The historical Phase 6 HTTP-only smoke listener was temporary and is no longer running.

## Build and run MAUI

On this checkout, a portable SDK and Windows MAUI workload are installed under the ignored `.tools/dotnet` folder. The machine-wide installer was canceled at elevation; the local installation succeeded without changing the global SDK. From the solution root:

```powershell
.\scripts\Run-Windows.ps1
# Build without launching:
.\scripts\Run-Windows.ps1 -BuildOnly
```

The launcher prefers that local SDK when present, otherwise uses `dotnet` from PATH. Its build-only mode was verified. Windows compilation and direct launch of the resulting executable were verified separately. It restores/builds only the MAUI Windows target using the project-specific `TodoAppTargetFrameworks` property. Do not use a global `TargetFrameworks` override: that also retargets referenced net10.0 libraries and can cause NETSDK1005. Windows App SDK files are bundled with the unpackaged app, so its runtime does not need a separate administrator installation. This is a development build, not a signed release installer.

On a fresh clone, `.tools` is absent. Install the matching .NET SDK and the platform workload using the [official setup guide](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?view=net-maui-10.0), then use the same launcher:

```powershell
dotnet workload install maui-windows
.\scripts\Run-Windows.ps1
```

If PowerShell reports that script execution is restricted, use the equivalent CLI commands through an installed SDK rather than changing system policy:

```powershell
dotnet build src/TodoApp.Maui/TodoApp.Maui.csproj -t:Run -p:TodoAppTargetFrameworks=net10.0-windows10.0.19041.0
```

For Android, install `maui-android` plus its SDK/JDK, select a device/emulator, and use:

```powershell
dotnet build src/TodoApp.Maui/TodoApp.Maui.csproj -t:Run -p:TodoAppTargetFrameworks=net10.0-android
```

To provision the broader workload set, `dotnet workload install maui` is available. A full `dotnet build TodoApp.slnx` evaluates all configured targets and requires corresponding tooling/remote Apple setup. Only Windows was compiled/launched in Phase 5; Android/iOS/Mac Catalyst remain unverified.

## Check the app yourself

1. Click **New task**, enter a title, description, priority, optional due date, and optional category. **Clear** removes a due date.
2. Click **Save task**. Select the task in the list to show **Edit**, **Complete/Reopen**, and **Delete** actions.
3. Edit fields and save; then edit again and cancel to confirm unsaved fields are discarded.
4. Check All, Pending, Completed, Due today, and High priority. The last two include completed tasks if their date/priority matches.
5. Delete a disposable test task and check both Cancel and Delete in the confirmation dialog.
6. Close and reopen the app. Confirm remaining tasks, categories, due dates, and completion state persist.
7. Check light/dark system themes and a narrow window. Phone layout/device behavior still needs native testing.

Use an existing category name to reuse a category visible among stored tasks, or enter a new name to create/assign one. Changing the name in a task editor reassigns that task; it does not rename a category globally. Category management and empty-category browsing are later work. Tasks are local to this device; no API or login is needed.

### Check account and server tasks

1. Configure PostgreSQL, the JWT secret, and both migrations using DATABASE.md. Start the API with its HTTPS profile and trust its development certificate on Windows.
2. Run the Windows client and open **Account & server** in the task-list toolbar. Its default API address is `https://localhost:7114/`.
3. Register a disposable account with a 12?128 character password, or sign in. Create, edit, complete/reopen, filter, and delete disposable **server** tasks.
4. Close/reopen the app and open Account again. A saved session must pass `/api/auth/me` before server tasks load. Verify real platform storage persistence here; automated tests substitute the platform store.
5. Stop the API and try Refresh. Check the friendly connection error, then go Back and confirm device-local CRUD still works. Restart the API and retry explicitly.
6. Sign out and sign into another test account. Server rows/drafts must clear; each account sees only its own server tasks. Device tasks stay on this device and are not assigned to either account.
7. Check expiry (the server defaults to 15 minutes), restart after sign-out, and an invalid HTTPS address. Changing the address signs out. No refresh token or automatic server-write retry exists.

The server workspace is online only. It does not upload, merge, cache, or queue device tasks. After a write with an uncertain result, successfully **Refresh** before another write; the request may already have committed. A confirmed create clears its draft even if the following refresh fails.

Passwords are cleared after attempts and never saved. Session token, account, expiry, and API origin are stored together in platform SecureStorage; Preferences stores only the API URL and a restoration-block flag. Storage errors show a message with no plaintext fallback. Failed token removal blocks automatic restoration on the next launch; retry clearing the session. Expiry is checked on protected operations. A current-session 401 signs out, while 403/network/timeout errors retain the session. Saved sessions require online verification after restart.

Android emulators default to `https://10.0.2.2:7114/`. A physical phone needs the PC's reachable LAN hostname/address, the same Wi-Fi, an API listener on that interface, a private-network firewall rule, and a certificate trusted by the device whose names match the address. Desktop localhost trust alone does not establish emulator/device trust. Use a properly trusted development certificate or HTTPS tunnel; the client has **no certificate-validation bypass**. Only root HTTPS base URLs are accepted, and redirects are not automatically followed. See [Microsoft's local web-service guidance](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/local-web-services?view=net-maui-10.0) and [SecureStorage platform requirements](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0).

Windows compilation and the local task-list toolbar were verified. Native Account layout, socket HTTPS authentication, actual SecureStorage restart/logout, light/narrow layouts, and mobile acceptance remain pending. Automated client/API/PostgreSQL tests use WebApplicationFactory's in-memory HTTPS transport and a fake platform store, so they do not prove those device checks.

## Database setup

**Device-local tasks need no database server or manual SQLite installation.** Package restore supplies the SQLite library. The MAUI registration chooses `Path.Combine(FileSystem.AppDataDirectory, "todoapp.db3")`; opening the task list queries the service and creates the directory, database, and version-1 schema when absent. Do not delete this file if you want to keep your tasks.

Local storage tests create temporary SQLite files and remove them afterward. SQLite schema versioning remains independent of server EF migrations. The API now uses PostgreSQL through TodoDbContext and the included InitialCreate migration. [DATABASE.md](docs/DATABASE.md) contains separate local/server setup instructions and the PostgreSQL test procedure. Store server credentials in development user secrets or ConnectionStrings__TodoDatabase, never source or the MAUI app.

## Try the local service in code

This example can run inside an async test/method after importing `TodoApp.Domain.Services`, `TodoApp.Domain.Enums`, and `TodoApp.Infrastructure.Services`. Choose an application-data path outside the checkout:

```csharp
var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "TodoApp", "todoapp.db3");
ILocalTaskService tasks = new SqliteLocalTaskService(path);
var task = await tasks.CreateAsync(new TaskDetails("Learn LINQ", Priority: TaskPriority.High));
await tasks.SetCompletedAsync(task.Id, true);
var completed = await tasks.GetTasksAsync(TaskFilter.Completed);
```

Create another SqliteLocalTaskService with the same path to read saved data again. Task ViewModels now receive ILocalTaskService through DI. Changes to returned objects alone are not saved: call UpdateAsync/SetCompletedAsync/DeleteAsync explicitly. InMemoryLocalTaskService remains a learning implementation and loses data when recreated.

## Learning path and Git

Start with [ARCHITECTURE.md](docs/ARCHITECTURE.md), then [LEARNINGS.md](docs/LEARNINGS.md). Read TaskItem alongside TaskItemTests to see how each business rule is checked. Follow [ROADMAP.md](docs/ROADMAP.md) after reviewing this phase. No later phase starts automatically.

This folder was not a Git repository at initial inspection. It is now initialized on `main`, with `origin` set to `https://github.com/DarryllPj77/Todo-app.git`. The foundation is recorded in commit `2b24e81` (`Create Todo App Phase 1 foundation`). See Git history and remote tracking status for subsequent documentation commits and publication status. No merge was performed.
