# Consumer navigation refactor

## Blueprint (2026-11-17)

Replace the fourteen-item Workspace tools action sheet with an adaptive MAUI flyout: Today, Tasks, Calendar, Focus, Views and Settings. A wide window keeps the sidebar visible; a narrow window uses the native compact navigation drawer. Retain NavigationPage stacks per section so switching sections does not discard a draft. Keep navigation per window and preserve runtime attach/resume/dispose behavior.

| Existing page / ViewModel | Destination and reuse |
| --- | --- |
| TaskListPage / TaskListViewModel; SyncPage / SyncWorkspaceViewModel | Tasks: explicitly separate device tasks from account tasks with the existing SQLite replica, queue, retry and conflict behavior |
| HierarchyPage / HierarchyViewModel; DependenciesPage / DependenciesViewModel | Task details: inline subtask list, parent and prerequisite editing using the existing guarded commands |
| PlanningPage / PlanningViewModel; RecurrencePage / RecurrenceViewModel | Task details: urgency/importance and repeat editor; Views: Eisenhower overview |
| RemindersPage / RemindersViewModel | Task details: scheduling; Settings: notification permission/retry/status |
| LocationRemindersPage / LocationRemindersViewModel | Task details: place editing; Settings: permissions/native/background run controls; retain explicit opt-ins and lifecycle cleanup |
| CalendarPage / CalendarViewModel; GoogleEventsPage / GoogleEventsViewModel | Calendar: local blocks and linked events; task details: schedule selected task |
| FocusPage / FocusViewModel | Focus timer/history, including selected-task entry; preserve foreground timer lifecycle |
| TaskViewsPage / TaskViewsViewModel | Views: smart filters, saved definitions and Eisenhower navigation |
| AccountPage / AccountViewModel; ServerWorkspaceViewModel | Settings Account for auth; Settings Advanced Server for origin/session verification; online-only task browser remains under Tasks |
| GoogleConnectionPage / GoogleConnectionViewModel; GoogleFeedPage / GoogleFeedViewModel | Settings Integrations: connection and monitoring configuration; Calendar retains event relationship actions |

Existing services/repositories, API contracts, authentication, PostgreSQL/SQLite schemas, sync protocol, Tailscale configuration and HTTPS validation must remain unchanged. Preserve the configured origin https://paja2025.tail0aad07.ts.net/ by reading existing ClientSettings rather than writing defaults. Appearance is a normal MAUI theme preference, not a backend setting.

Task-context controls must pin workspace, task ID and account/session generation, preserve conflict handling, and never retarget another account. New tasks save their core fields before configuring capabilities requiring a persisted ID. Existing feature editors are reused within task details where safe; timer/native lifecycle operations retain their original pages or explicit settings surfaces. No provider/permission request runs from navigation alone.

## Verification plan

Run the solution/core tests appropriate to changed presentation behavior, Windows build and Android build/audit. Add meaningful coverage for task-context identity isolation, Today queries and hierarchy/sync boundaries. Inspect the Windows navigation and task-context screens; report Android compilation separately from physical-device acceptance. Record native/provider/permission checks that were not performed. Preserve existing uncommitted backend setup files and unrelated .vscode/. No commit or push is authorized by this refactor.

Implementation is committed as b025101. Verification results and remaining limits are recorded in DEVELOPMENT_PROGRESS.md. The user subsequently authorized pushing the committed refactor and follow-up setup/documentation changes.

## Verification result

- Windows-targeted solution and Windows MAUI builds: passed, 0 warnings/errors.
- Selected Android Debug build and APK audit: passed, 0 compiler warnings/errors; physical phone installation/navigation remains pending.
- Tests: 653 passed, 59 PostgreSQL integration cases skipped, 0 failed. Four new cases exercise actual SQLite relationships, workspace/account isolation, Today and classification. External/provider/native behavior is not certified by these tests.
- Desktop sidebar, saved task display and Settings grouping were observed. Computer Use was stopped by the user before final task-details/narrow-window checks; those checks and mobile device acceptance remain incomplete.
- Existing guarded feature commands were reused. Custom recurrence is limited to the supported daily/weekly/monthly rules and interval; no new recurrence engine or automatic merge was introduced. Save core fields before configuring task-ID-dependent properties. Device-only tasks stay separate, and account edits retain their queue/conflict/retry semantics.
- Remaining review: selected-task editors and draft preservation on actual Windows/Android devices, native reminder/location lifecycle and Google provider acceptance, and same-account two-device CRUD. No later major phase is started by this refactor or push.
