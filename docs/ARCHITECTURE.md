# Doba — architecture

Doba is a local-only daily planner for Windows built with .NET 11, Avalonia 12 and SQLite. This document is the
contract every work package conforms to. The brief is `docs/KICKOFF.md`; the design sources of truth are
`docs/design/design-reference.dc.html` (markup + reference behaviour) and `docs/design/motion-spec.md`.

## 1. Solution layout

```
Doba.sln
src/Doba.Core/        Pure domain: model, date math, in-memory store, projections, navigation, dialog logic, DemoData.
src/Doba.Storage/     SQLite (Microsoft.Data.Sqlite, hand-written SQL, WAL), settings, daily backup, JSON export/import.
src/Doba.App/         Avalonia app (assembly "Doba"): shell, views, controls, theme tokens, Motion, platform services.
tests/Doba.Core.Tests/     Unit tests, golden tests against the prototype's JS, projection perf test.
tests/Doba.Storage.Tests/  Repository / backup / JSON round-trip tests on temp folders.
tests/Doba.App.Tests/      Headless UI tests (Avalonia.Headless.XUnit + real Skia) and the screenshot harness.
docs/                      ARCHITECTURE, DECISIONS, PROGRESS, HANDOFF, design/, screenshots/.
```

Dependency direction: `App → Storage → Core`, `App → Core`. Core has no package references.

## 2. Data flow

```
 startup: DobaDatabase.Open → Load() ─▶ TaskStore (in memory, indexed) ◀── UI-thread mutations
                                             │ Changed event (one per mutation)
                                             ▼
                         screen view-models re-run their Projector.* function (<5 ms)
                                             │ immutable records (TaskRow, TaskGroup, WeekModel ...)
                                             ▼
                      KeyedSync reconciles ObservableCollections by key → only changed rows update
 writes: TaskStore ─▶ ITaskPersistence (write-behind queue, background writer) ─▶ SQLite; flushed on close
```

* **Store** (`Doba.Core.Store.TaskStore`): the only mutable task state. Indexes: by date (single-day tasks),
  multi-day list sorted by (Date, Id), undated list. Ids grow monotonically; Id order = the prototype's array order.
* **Projections** (`Doba.Core.Projections.Projector`): pure functions `(ITaskReader, ProjectionContext, …) → record`.
  They own *every* "what goes where" rule: grouping, sorting (`byTime`, `byDate`), lanes, "+N more" fitting from
  measured sizes (`WeekMetrics`, `MonthMetrics`), labels ("Day 2 of 3", "Mon 16 Nov", "6 tasks · 4 done · 2 left").
  Views never sort, filter or format dates themselves.
* **Local updates**: a view-model holds `ObservableCollection<TaskRowViewModel>` (and group/cell VMs). After a store
  change it re-projects and calls `KeyedSync.Apply(existing, newRecords, key, create, update)`, which updates rows in
  place by key, inserts/removes the differences and never clears the collection. Completing a task therefore changes
  one row's properties and the counters, not the visual tree.
* **Time** is injected: `TimeProvider` everywhere (`clock.Today()`, `clock.TimeOfDay()` from
  `Doba.Core.Time.TimeProviderExtensions`). A `DayClock` service in the App ticks once a minute (now-line) and raises
  `DayChanged` at local midnight (rollover: re-project, run carry-over, run the daily backup).
* **Settings**: `AppSettings` record (Core) ← `ISettingsPersistence` (Storage) ← `ISettingsService` (App). Changes
  apply live.

## 3. Doba.Core

| Namespace | Contents |
|---|---|
| `Model` | `TaskItem` (immutable record), `Priority` (+ `Next()`, `DisplayName()`), `AppSettings` (+ enums `Accent`, `ThemeMode`, `Density`, `LaunchView`), `Tags.Normalize` |
| `Time` | `DateMath` (week start for any first day, ISO week, labels), `TimeProviderExtensions`, `FixedTimeProvider` |
| `Store` | `ITaskReader`, `ITaskStore`, `TaskStore`, `ITaskPersistence`, `NullTaskPersistence`, `ISettingsPersistence`, `TaskStoreChangedEventArgs` |
| `Projections` | `ProjectionContext`, `TaskRow`, `TaskGroup`, `SpanBar`, `NavTarget`, `Inbox/Today/Day/Week/Month/Year/Sidebar` models, `SearchHit`, `ScheduleModel` + `ScheduleLayout`, `Projector` |
| `Navigation` | `AppView`, `SettingsTab`, `TransitionKind`, `NavState`, `NavResult`, `Navigator`, `HeaderModel`, `Crumb`, `HeaderProjection` |
| `Editing` | `TaskDraft`, `DraftPopover`, `MiniCalendarDay`, `TaskDialog` (dialog rules, pill labels, wheel commit) |
| `Demo` | `DemoData` — the prototype's seed (incl. the mulberry32 fill, seed 46) and fixed clock 2026-11-15 22:26 |

Rules ported from the prototype (read `design-reference.dc.html` lines 929–1571 for the originals):

* `byTime`: timed before untimed, then time ascending, then priority descending (ASAP 4 … none 0), then Id.
* Week rows: row 1 = days 0–3 of the week, row 2 = days 4–6 + a dimmed preview of the next week's first day.
  Spans get lanes greedily in (Date, Id) order within each row. Week start comes from settings (Monday or Sunday).
* Month: up to two span lanes per week; per-cell capacity `max(1, floor((H − 34 − lanes·24 − 8) / 25))`; when a cell
  overflows, `cap − 1` items are shown plus "+N more".
* Year: spans in the month (at most 3 if more than 4, then "+N more"), then upcoming open single-day tasks up to 4 rows
  (only for non-past months without overflow).
* Week numbers: ISO week of the Monday inside the displayed week (identical to the prototype for Monday starts).

## 4. Doba.Storage

* `DobaDatabase.Open(DataLocation)` creates `%LOCALAPPDATA%\Doba\doba.db`, sets `journal_mode=WAL`,
  `synchronous=NORMAL`, migrates by `PRAGMA user_version`.
* Schema v1: `tasks(id INTEGER PRIMARY KEY, title, notes, date TEXT NULL 'yyyy-MM-dd', end_date TEXT NULL,
  time TEXT NULL 'HH:mm', duration INTEGER, priority INTEGER, tag TEXT NULL, done INTEGER)`,
  `tags(name TEXT PRIMARY KEY, position INTEGER)`, `settings(key TEXT PRIMARY KEY, value TEXT)`.
* `Load()` reads everything once. `Tasks` (`ITaskPersistence`) and `Settings` (`ISettingsPersistence`) enqueue writes
  to a single background writer (a `Channel`), batching into transactions; `FlushAsync` waits; `DisposeAsync` flushes.
* `Backup.RunDailyAsync` uses `VACUUM INTO` to `Backups/doba-yyyy-MM-dd.db`, keeps 7.
* `JsonTransfer` uses a `JsonSerializerContext` (source generation, no reflection), format `{ "format": "doba", "version": 1, tasks, tags, settings }`.

## 5. Doba.App

```
App.axaml(.cs)          Application; builds AppServices (composition root) and the main window.
AppServices.cs          Hand-written composition root: clock, store, database, settings, platform, DayClock.
Program.cs              Entry point; flags: --demo (DemoData + fixed clock, no disk), --data <folder>, --perf.
Theme/                  Tokens.axaml (colours, brushes, radii, font sizes, spacing), Fonts, Controls/*.axaml (ControlThemes).
Motion/                 Motion (tokens), Animator (composition/transition helpers), ContentTransitions.
Platform/               IAutostart, IGlobalHotkey, ISystemPreferences, IShellIntegration; Windows/ implementations; no-op fallbacks.
Services/               ISettingsService, DayClock, KeyedSync, TaskActions (toggle/cycle/move, completion sound).
Shell/                  MainWindow (custom chrome), TitleBar, Sidebar, PageHeader, MainViewModel (NavState, current screen).
Screens/<Name>/         <Name>View.axaml + <Name>ViewModel.cs for Inbox, Today, Week, Month, Year, Day, Settings.
Dialogs/                TaskDialog (+ Date/Time/Priority/Tag popovers, TimeWheel control), SearchPalette.
Controls/               Reusable: TaskCheckBox, PriorityIcon, ProgressLine, Kbd, SegmentedSwitch, ToggleSwitch, Pill, TaskCard, TaskListRow.
```

* **MVVM**: CommunityToolkit.Mvvm source generators (`[ObservableProperty]`, `[RelayCommand]`); compiled bindings
  everywhere (`x:DataType` on every view); no reflection binding, no DI container.
* **Theme tokens**: every colour, radius, font size and spacing comes from `Theme/Tokens.axaml` (`Color.*`, `Brush.*`,
  `Radius.*`, `FontSize.*`); controls use our own `ControlTheme`s (no Fluent theme). Accent is a dynamic resource
  (`Brush.Accent`, `Brush.Accent10`, `Brush.Accent15`, `Brush.Accent25`) updated from settings.
  Fonts: Geist and Geist Mono embedded (`avares://Doba/Assets/Fonts#Geist`).
* **Motion**: every duration/easing comes from `Doba.App.Motion.Motion`. Content transitions animate only opacity
  and render transforms (translate/scale) — on the composition visual where possible — never Margin/Width/layout;
  progress bars animate a `ScaleTransform.ScaleX` (origin left). The zoom blur is optional (dropped if costly).
  `Motion.SystemAnimationsEnabled` mirrors the OS setting; when false every duration is 0.
* **Platform**: Windows-only features live in `Platform/Windows/*` behind the interfaces, use `LibraryImport`
  (AOT-safe) and are selected at runtime with `OperatingSystem.IsWindows()`. Headless/Linux uses no-op fallbacks.
* **Window chrome**: `ExtendClientAreaToDecorationsHint`, our own 40 px title bar; caption buttons and the drag area
  are marked with `WindowDecorationProperties.SetElementRole` (Avalonia 12) so Windows snap layouts/hit-testing work.

## 6. Testing and verification

* `dotnet build -warnaserror` (trim + AOT analyzers on for shipping projects) and `dotnet test` (Microsoft Testing
  Platform; xunit v3 3.2.x — see DECISIONS).
* Core golden tests compare `Projector` output with the prototype's own JS functions executed under Node on the same
  seed (`tests/Doba.Core.Tests/Golden/`).
* Screenshots: `tests/Doba.App.Tests` renders the real `MainWindow` headlessly with Skia at 1600×1000 using
  `DemoData` (`--demo` equivalent) and writes `docs/screenshots/<name>.png`. Reference images of the prototype are in
  `docs/design/screens/`.
* A grep-based test fails on literal colours (`#RRGGBB`) or durations outside `Theme/` and `Motion/`.
* Performance: projection perf test (10k tasks, < 5 ms each with CI margin), `--perf` logs time-to-first-frame,
  CI prints it.
