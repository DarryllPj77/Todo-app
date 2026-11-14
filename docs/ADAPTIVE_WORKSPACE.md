# Adaptive workspace - Phase 12G

The home screen is explicitly the **Device workspace**. At 1180 logical pixels of available grid width it shows categories, the task list, and read-only details together. Below that width it shows a category picker and **Tasks / Details** navigation. The same selected task survives resize and a successful refresh while it remains visible. Removing it through a search, category, filter, or deletion clears its details and action commands.

Details show description, completion, priority, due date, category/tags, planning quadrant, recurrence, parent/prerequisite IDs, and updated time. **Edit task** opens the existing editor; layout changes never save an edit. Complete/reopen and confirmed deletion use the existing reminder-aware local service and its hierarchy/dependency/recurrence rules. Repeating occurrence completion remains in **Tools > Recurring tasks**. TimeSpent and history remain in **Tools > Focus & history**.

## Organization and navigation

- Categories come from the currently loaded task filter. Names group case-insensitively; virtual All categories and Uncategorized groups are distinct from actual categories with those names. If a selected group disappears on refresh, the screen returns to All categories.
- Category counts follow the completion/due/priority filter, before category/search narrowing. The task count shows the final matching list. Search is a trimmed, literal, case-insensitive substring over actual title, description, category and tags. It does not interpret SQL, regex or display fallback text.
- Choose **Tools** for Saved views, Task hierarchy, Dependencies, Urgency & importance, Recurring tasks, Focus & history, Reminders, Synced tasks, or Account & server. This replaces the nine-item toolbar.
- Synced tasks retains its existing account/origin isolation, cache, drafts, conflicts and sync controls. The home pane does not combine account tasks with device tasks or upload device tasks. Advanced screens keep their own workspace selectors.
- Refresh after another window changes tasks. This increment does not add live database subscriptions, category renaming/deletion, user-adjustable pane widths, or saved layout preferences.

## Run and check

Use `powershell -ExecutionPolicy Bypass -File .\scripts\Run-Windows.ps1` from the repository root after closing an older build that locks its output. No API or database migration is needed for this UI increment. Existing account features still require the documented eight server migrations and protocol 6.

For an isolated synthetic-data layout check in a **Debug** build:

```powershell
$env:TODOAPP_PREVIEW_DATA_DIRECTORY = Join-Path (Get-Location) '.tools\workspace-preview'
try { powershell -ExecutionPolicy Bypass -File .\scripts\Run-Windows.ps1 }
finally { Remove-Item Env:TODOAPP_PREVIEW_DATA_DIRECTORY -ErrorAction SilentlyContinue }
```

The explicit preview directory holds separate SQLite files. When empty it receives three synthetic device tasks. Client settings/tokens use process memory, HTTP requests fail locally, notification scheduling is disabled, and lifecycle timer runtimes are not attached. The window is titled **Todo layout preview**. Release builds ignore this environment variable. Preview is for layout checks, not authentication, focus lifecycle or notification acceptance. Do not point it at an existing task-data directory.

## Acceptance checklist

- [ ] On Windows, inspect wide three-pane and compact navigation, category selection, search, selected details and repeated resizing.
- [ ] Check edit/cancel/save/back navigation, completion, confirmed deletion, and another-window refresh with actual device data.
- [ ] Check long task/category text, long notes/IDs, many categories/tasks, empty states and scrolling at narrow widths and short heights.
- [ ] Check keyboard-only navigation, screen reader labels/selection announcements, large text/high contrast, and light/dark themes.
- [ ] Build and inspect Android/iOS/Mac Catalyst with their native toolchains; exercise phone rotation, keyboard, back navigation and lifecycle.
- [ ] Follow each earlier Phase 12 feature document for its still-open native checks. Passing portable tests and a Windows build do not close these gates.

Task/sync SQLite remain version 6, saved views version 3, reminders/focus version 1; PostgreSQL remains eight migrations and sync protocol 6. There are no schema, API, queue, receipt, or token-format changes.

## Verification recorded on 2026-11-14

All 410 tests passed, 0 skipped (351 portable cases and 59 PostgreSQL facts), including six new workspace cases and the existing editor/list cases. The isolated Windows solution/XAML build passed with 0 warnings/errors. Test databases were cleaned up and the repository cluster was stopped.

The native Debug preview showed the wide three-pane layout in the current dark theme, category filtering from three tasks to Work's one task, and selected task details with notes/tags/actions. The user stopped Computer Use with Escape before compact verification completed. Compact state transitions are covered by portable tests; actual compact rendering, mobile builds, edit flows, theme/accessibility coverage, repeated resizing and short-height behavior remain unchecked. No notification/account/focus lifecycle acceptance was performed through the preview.
