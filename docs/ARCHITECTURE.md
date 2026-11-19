# Doba — architecture

Doba is a local-only daily planner for Windows built with .NET 11 (C# 15), Avalonia 12.1.6, CommunityToolkit.Mvvm and
SQLite (Microsoft.Data.Sqlite), published with NativeAOT for `win-x64`. This document is the contract the code
follows: where each kind of logic lives, the names other parts rely on, and the rules that keep the app fast and
faithful to the design. The brief is `docs/KICKOFF.md`; the design sources are `docs/design/design-reference.dc.html`
(markup + reference behaviour) and `docs/design/motion-spec.md`. Decisions are in `docs/DECISIONS.md`; tokens and
controls in `docs/design-system.md`.

## 1. Solution layout

```
Doba.sln
src/Doba.Core/        Pure domain: model, date math, in-memory store, carry-over, projections, navigation,
                      dialog rules, DemoData. No package references.
src/Doba.Storage/     SQLite (hand-written SQL, WAL), write-behind writer, settings persistence, daily backup,
                      JSON export / import (source-generated).
src/Doba.App/         Avalonia app (assembly "Doba", WinExe): startup, composition, shell, screens, dialogs, controls,
                      theme, motion, platform services, lifecycle services, diagnostics.
tests/Doba.Core.Tests/     Unit tests, golden tests against the prototype's script, projection timing.
tests/Doba.Storage.Tests/  Database / writer / backup / JSON tests on temp folders.
tests/Doba.App.Tests/      Headless UI tests (Avalonia.Headless.XUnit + real Skia), screenshots, perf data.
scripts/perf.ps1      Performance measurement of the NativeAOT build.
tools/design-render/  Renders the prototype to docs/design/screens (Node + Playwright; not part of the build).
docs/                 ARCHITECTURE, DECISIONS, PROGRESS, HANDOFF, MOTION-REVIEW, design-system, KICKOFF,
                      design/, screenshots/.
```

Dependency direction: `App → Storage → Core`, `App → Core`. Shared build settings are in `Directory.Build.props`
(net11.0, C# 15, nullable, warnings as errors, `latest-recommended` analyzers, invariant globalization; trim, AOT and
single-file analyzers on for every non-test project) and versions in `Directory.Packages.props`.

## 2. Data flow

```
 Program.Main: parse flags → claim the data folder → preload (pool thread): DobaDatabase.Open → Load() → TaskStore
                                             │
 UI-thread mutations (TaskActions, dialog) ──▶ TaskStore (in memory, indexed) ── Changed (one event per mutation)
                                             │                                   │
                                             │                    screen view-models re-run Projector.* (< 5 ms)
                                             │                                   │ immutable records
                                             │                    KeyedSync updates bound collections by key
                                             ▼
                      ITaskPersistence (DobaDatabase.Tasks): write-behind queue → one background writer → SQLite
                      flushed on hide-to-tray, Quit, Exit and Windows session end
```

* **Store** (`Doba.Core.Store.TaskStore`): the only mutable task state. Indexes: single-day tasks by date, multi-day
  tasks sorted by (Date, Id), undated tasks. A new task gets the largest Id + 1 (`checked`); Id order is the
  prototype's array order and the final sort key. Every task loaded, added or updated is normalised (an End not after Date is
  dropped, Time only on dated single-day tasks, an undefined Priority becomes None). Every mutation raises exactly one
  `Changed` event; `UpdateMany` raises one `Batch` event for several tasks; identical updates raise nothing.
* **Projections** (`Doba.Core.Projections.Projector`): pure functions `(ITaskReader, ProjectionContext, …) → record`.
  They own every "what goes where" rule: grouping, sorting, lanes, "+N more" fitting from measured sizes, labels.
  Views never sort, filter or format dates themselves. Where rules have drifted into App view-models, HANDOFF §5 lists
  them.
* **Local updates**: a screen view-model re-projects after a store change and calls `KeyedSync.Apply(existing, records,
  key, create, update)`, which updates rows in place by key, inserts / removes / moves the differences and never
  clears the collection. Completing a task changes one row's properties and the counters, not the visual tree.
* **Time** is injected: `TimeProvider` everywhere (`clock.Today()`, `clock.TimeOfDay()` from
  `Doba.Core.Time.TimeProviderExtensions`). `DayClock` (App) ticks once a minute and raises `DayChanged` when the local
  date changes; `Check()` also runs on window activation and on `WM_TIMECHANGE`. On a day change: re-projection,
  carry-over, daily backup, and Inbox / Today anchors move to the new day.
* **Settings**: `AppSettings` (Core record) ← `ISettingsPersistence` (Storage, the `settings` table) ←
  `ISettingsService` (App, `Changed` event). Changes apply live.

## 3. Doba.Core

| Namespace | Contents |
|---|---|
| `Model` | `TaskItem` (immutable record), `Priority` (+ `Next()`, `DisplayName()`), `AppSettings` (+ `Accent`, `ThemeMode`, `Density`, `LaunchView`), `Tags.Normalize` |
| `Time` | `DateMath` (week start for any first day, ISO week, labels; month names as read-only lists + `MonthName(m)` / `MonthAbbrev(m)`), `TimeProviderExtensions`, `FixedTimeProvider` |
| `Store` | `ITaskReader`, `ITaskStore` (`Add`, `Update`, `Delete`, `ToggleDone`, `CyclePriority`, `MoveToDate`, `UpdateMany`, `AddTag`, `ReplaceAll`), `TaskStore`, `TaskChangeKind` (`Added`, `Updated`, `Removed`, `Reset`, `TagsChanged`, `Batch`), `ITaskPersistence`, `NullTaskPersistence`, `ISettingsPersistence`, `CarryOver` |
| `Projections` | `ProjectionContext(Today, Now, WeekStart, FilterTag)`, `TaskRow`, `TaskGroup`, `SpanBar`, `NavTarget`, the Inbox / Today / Day / Week / Month / Year / Sidebar models, `SearchHit`, `ScheduleModel` + `ScheduleLayout`, `Projector` |
| `Navigation` | `AppView`, `SettingsTab`, `TransitionKind`, `NavState`, `NavResult`, `Navigator`, `HeaderModel`, `Crumb`, `HeaderProjection` |
| `Editing` | `TaskDraft`, `DraftPopover`, `MiniCalendarDay`, `DateQuickOption`, `TaskDialog` (dialog rules, pill labels, quick options, days stepper, wheel commit) |
| `Demo` | `DemoData`: the prototype's seed (incl. the mulberry32 fill, seed 46) and the fixed clock 2026-11-15 22:26 |

`Projector` entry points (all pure, all under 5 ms at 10,000 tasks):

```csharp
InboxModel   Inbox(ITaskReader store, ProjectionContext ctx);
TodayModel   Today(ITaskReader store, ProjectionContext ctx);
DayModel     Day(ITaskReader store, ProjectionContext ctx, DateOnly date);
WeekModel    Week(ITaskReader store, ProjectionContext ctx, DateOnly anchor, WeekMetrics metrics);
MonthModel   Month(ITaskReader store, ProjectionContext ctx, DateOnly anchor, MonthMetrics metrics);
YearModel    Year(ITaskReader store, ProjectionContext ctx, int year);
SidebarModel Sidebar(ITaskReader store, ProjectionContext ctx);
IReadOnlyList<SearchHit> Search(ITaskReader store, ProjectionContext ctx, string query, int limit = 50);
```

Measured inputs: `WeekMetrics(RowHeight, CardHeight(title), CardGap, MoreReserve, HeaderHeight, LaneHeight)` with an
optional per-task `TaskCardHeight` (not yet passed by the App); `MonthMetrics(AvailableHeight, HeaderHeight,
DayNumberHeight, LaneHeight, ItemHeight, MinListRoom)`. Degenerate sizes are safe (a NaN or non-positive Week row fits
no card; a Month body shorter than its header gives row height 0).

Rules ported from the prototype (`design-reference.dc.html` script lines 929–1571 hold the originals):

* `byTime`: timed before untimed, then time ascending, then priority descending (ASAP 4 … none 0), then Id.
* Week rows: row 1 = days 0–3 from the configured week start, row 2 = days 4–6 + a dimmed preview of the next week's
  first day. Spans get lanes greedily in (Date, Id) order within each row.
* Month: up to two span lanes per week (a span needing a third is dropped, as in the prototype); per-cell capacity
  `max(1, floor((H − 34 − lanes·24 − 8) / 25))`; on overflow `cap − 1` items plus "+N more"; a cell with less than
  `MinListRoom` below its lanes returns no items, `MoreCount` = all and `MoreInHeader = true`.
* Year: spans in the month (at most 3 if more than 4, then "+N more"), then upcoming open single-day tasks up to 4 rows
  (non-past months without overflow). The App trims these to the measured cell (`Screens/Year/YearRoom`).
* Week numbers: ISO week of the Monday inside the displayed week.
* Search: substring of the title, open before done, then by date (undated last), then Id; the best `limit` hits are
  kept while scanning (no full sort).
* `CarryOver.Run(store, today)`: open single-day tasks dated before today move to today in one `UpdateMany`.

## 4. Doba.Storage

* `DataLocation(Folder)`: `doba.db` and `Backups/` inside it; `DataLocation.Default()` = `%LOCALAPPDATA%\Doba`.
* `DobaDatabase.Open(DataLocation)` creates the folder and database, sets `journal_mode=WAL` and `synchronous=NORMAL`,
  migrates by `PRAGMA user_version` (a newer version is refused with `NotSupportedException`). `Load()` reads
  everything once (tasks through SQLitePCL's low-level API). `Tasks` (`ITaskPersistence`) and `Settings`
  (`ISettingsPersistence`) enqueue writes; `WriteFailed` reports each failure; `DisposeAsync` flushes and closes.
* Schema v1: `tasks(id INTEGER PRIMARY KEY, title, notes, date 'yyyy-MM-dd' NULL, end_date NULL, time 'HH:mm' NULL,
  duration, priority, tag NULL, done)`, `tags(name PRIMARY KEY, position)`, `settings(key PRIMARY KEY, value)`.
* `Internal/WriteBehindWriter`: one background writer over an unbounded `Channel`; a burst of up to 4096 ops commits
  in one transaction. A burst refused by the database as a whole (BUSY, LOCKED, FULL, IOERR, READONLY, CORRUPT,
  CANTOPEN, NOTADB, NOMEM) fails as a whole; only per-row errors are replayed op by op. Failed ops are not retried.
* `Backup.RunDailyAsync(db, today, keep = 7)`: `VACUUM INTO` a temp file on the writer connection, flush, rename to
  `Backups/doba-yyyy-MM-dd.db`; prune to the newest 7 exact names and delete stale `doba-<other day>.db.tmp` files.
* `JsonTransfer`: export / import with a `JsonSerializerContext` (no reflection). Format
  `{ "format": "doba", "version": 1, "exportedAt", "tasks", "tags", "settings" }`, camelCase enum strings. Import
  validates the document (format and version, ids 1..2^53, every task field, tags, settings), normalises tags like
  typed ones, and reports problems as `FormatException` with our own sentences.

## 5. Doba.App

### 5.1 Startup and composition

```
Program.Main
  StartupFlags.TryParse(args)            strict; error → stderr + usage, exit 2
  SingleInstance.Claim(folder)           not for --demo / --selfcheck; second launch → SignalPrimary(), exit 0
  AppStartup.Begin(flags, instance)      PerfProbe on for --perf; AppServices.StartPreload: open + load + index on a pool thread
  BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)
    App.Initialize                       XAML; grayscale antialiased, unhinted text on every top level
    App.OnFrameworkInitializationCompleted
      --selfcheck → SelfCheck.Start      demo window + nine platform checks, exit 0 / 1
      otherwise   → App.StartApp
        AppStartup.OpenServices          AppServices.FromStartup(flags, preload); a failure → StartupFailedException
        App.Launch                       ShellComposition.CreateMainWindow, window icon, FitToScreen,
                                         new AppLifecycle(…).Start()   (also run by the startup-order tests)
        ListenForShowRequests            a second launch → lifecycle.ShowWindow
        desktop.MainWindow = window      unless a --minimized start goes straight to the tray
  catch StartupFailedException → StartupError.Report (message box + stderr; stderr only with --perf), exit 3
```

* `AppServices` is the hand-written composition root (no DI container): `Clock`, `Store`, `Settings`, `Platform`,
  `DayClock`, `Database` (null in demo), `DataFolder`, `SaveFailed` event, `FlushAsync`, `DisposeAsync`.
  `CreateDemo(settings?, platform?)` = DemoData + fixed clock + no persistence (used by `--demo`, `--selfcheck` and
  the UI tests). The platform set is built from `PlatformPlan.For(demo, customDataFolder)` after the data has loaded.
* `ShellComposition.CreateMainWindow(app, application, start?)`: `ThemeService.Apply(settings)`, `DayClock.Start()`,
  `new MainViewModel(app, theme, start)`, `MainWindow.Attach(vm)`, `MotionSync.Track(window)`.
  `ShellComposition.Transitions` defaults to `ContentTransitions`.

### 5.2 Folders

```
AppStartup.cs           StartupFlags (parse, usage), StartupFailedException, AppStartup (flags, stopwatch, claim, preload).
AppServices.cs          Composition root; LoadedStore.
App.axaml(.cs)          Application: text rendering setup, StartApp, Launch.
MainWindow.axaml(.cs)   Custom chrome, backdrop (ApplyBackdrop), FitToScreen, launch entrance.
Shell/                  Contracts (lead-only: ShellContracts.cs, OverlayViewModel.cs, IAppCommands.cs), ShellComposition,
                        MainViewModel, TitleBar, PageHeader(+ViewModel), ScreenHost, ScreenFactory, OverlayHost,
                        OverlayTransitions, KeyboardShortcuts, Sidebar(View/ViewModel), SettingsSidebarView,
                        SidebarItem, SidebarHighlight, SidebarTabViewModel, TagItemViewModel, WindowPlacement.
Screens/<Name>/         View + ViewModel for Inbox, Today, Week, Month, Year, Day, Settings (+ helpers: WeekLayout,
                        WeekRowsPanel, WeekCardMeasurer, ZoomOrigin, MonthLayout, YearRoom, DayEntrance, SettingsRows…).
                        Base: ScreenViewModel + ScreenServices (lead-only).
Dialogs/                TaskDialogView(+ViewModel), TaskDialogItems; Popovers/ (Date, Time, Priority, Tag, PopoverAnchor,
                        WeekColumns); Search/ (SearchPaletteView, SearchPaletteViewModel, SearchHitViewModel).
Controls/               TaskCheckBox, PriorityIcon, Icon, Kbd, Pill, Card, ProgressLine, SegmentedSwitch, ToggleSwitch,
                        FocusRing, StrikeText, TaskCard, TaskListRow, TaskGroupList, AddTaskButton, SpanBarButton,
                        ScheduleTimeline, ScheduleScroll, TimeWheel, PixelCentring, SnapScope.
ViewModels/             TaskRowViewModel, TaskGroupViewModel, EntranceExpiry (shared row / group view-models).
Theme/                  Tokens.axaml, Theme.axaml (merges the rest), Styles.axaml, Icons.axaml, Controls/*.axaml,
                        ThemeService, AccentPalette, ThemeConverters, Gallery/ControlsGallery (test rendering).
Motion/                 Motion (tokens), MotionTimes, Animator (+ MotionHandle, MotionState), ContentTransitions,
                        SidebarCollapse, MotionSync.
Platform/               Interfaces + PlatformServices / PlatformPlan, AvaloniaTrayIcon, AutostartCommand, CompletionSound,
                        SelfCheck, StartupError; Windows/ (NativeMethods, PlatformWindow, WindowsAutostart,
                        WindowsHotkey, WindowsPreferences, WindowsShell).
Services/               AppLifecycle, DataMaintenance, DayClock, KeyedSync, TaskActions, SettingsService
                        (ISettingsService), SingleInstance, DataLog.
Diagnostics/            PerfProbe, PerfSession, FrameMeter, FrameStats, LongFrames, WeekScenario, OverlayScenario.
```

### 5.3 Shell

* **MVVM**: CommunityToolkit.Mvvm source generators (`[ObservableProperty]` on partial properties, `[RelayCommand]`);
  compiled bindings everywhere (`x:DataType` on every view and template; `AvaloniaUseCompiledBindingsByDefault`).
* **`MainViewModel`** (`IAppCommands`, `IShellNavigation`): holds `Nav` (`NavState`), `Current` (screen view-model),
  `FilterTag`, `Overlay`, `IsSidebarCollapsed` (memory only), `SaveProblem` (the "Changes not saved" text), `Sidebar`,
  `Header`. It applies `Navigator` results, raises `Navigated` exactly once per step whose kind is not `None`, and
  hands the swap to `ContentSwapper`, which the window points at `ShellComposition.Transitions.Run`. Screen, header
  and sidebar highlight all change inside the swap (`Present`). `ScreenFor(view)` creates one cached view-model per
  view through `ScreenFactory`.
* **Contracts** (`Shell/ShellContracts.cs`, lead-only):

```csharp
public interface IShellNavigation : INotifyPropertyChanged
{
    NavState Nav { get; }
    void GoSection(AppView view);   void GoCalendar();          void SetZoomLevel(AppView level);
    void Shift(int direction);      void GoToday();
    void OpenSettings();            void BackFromSettings();    void SetSettingsTab(SettingsTab tab);
    bool IsSidebarCollapsed { get; }                            void ToggleSidebar();
    string? FilterTag { get; }      void ToggleTagFilter(string tag);   string? AddTag(string raw);
    void OpenSearch();
    event EventHandler<NavigatedEventArgs>? Navigated;          // From, To, Kind, Origin (0..1 of the content area)
}
public interface IAppCommands      // what a screen may ask of the shell
{
    void ZoomTo(AppView view, DateOnly anchor, Point? origin = null);
    void NewTask(DateOnly? date = null, bool hasExplicitDate = false);
    void EditTask(long id);
}
public interface IContentTransitions
{
    void Run(TransitionKind kind, Point? origin, Action swap, ContentTransitionTargets targets);
    void Launch(ContentTransitionTargets targets);
}
public abstract partial class OverlayViewModel : ObservableObject
{
    public event EventHandler? CloseRequested;
    protected void RequestClose();
    public virtual bool OnEscape() => false;   // true = handled inside (closed a popover); false = host closes
    public virtual void OnClosed() { }         // after the exit animation; the task dialog writes the store here
}
public interface IOverlayView { void FocusInitial(); }
```

  `Run` invokes `swap` exactly once — for section steps after the exit phase, otherwise before the entrance; a new
  `Run` cancels the running one and invokes a pending swap at once; content always ends at opacity 1 with an identity
  transform (cancel, completion, animations off).
* **ScreenHost** creates each screen view on its first visit and keeps it (`IsVisible=false` when hidden); a
  transition never rebuilds a visual tree. **ScreenFactory** maps view → view-model → view with `switch` expressions
  (no reflection), so NativeAOT sees every type.
* **OverlayHost** hosts the task dialog, its popovers and the search palette as in-window panels (never `Popup`
  windows). It owns the shade, the backdrop blur (`BlurEffect` on the content behind, σ from
  `Size.Overlay.BackdropBlur`, following the shade's opacity), Esc routing (`OnEscape()` first), focus (parked on the
  panel during the entrance, typed text buffered, then `FocusInitial`), and focus return after `OnClosed` has written
  (to the opener, or New task if it is gone; with a focus ring only if the opener had one). `AnimatedOverlayTransitions`
  plays the spec's open / close / replace motion.
* **KeyboardShortcuts**: window-level keys on the bubbling `KeyDown`, so a focused control handles its own keys first;
  virtual key with a physical-key fallback; G chord on the app clock's timestamps; key-repeat and character
  suppression rules (DECISIONS "Keyboard scope", "Keyboard matching").
* **Window**: `ExtendClientAreaToDecorationsHint`, our own 40 px `TitleBar`; caption buttons and the drag area carry
  `WindowDecorationProperties.SetElementRole` roles (and plain click handlers). `WindowPlacement.Fit` sizes the opening
  window to the screen's working area.

### 5.4 Screens, dialogs, controls

* A screen view-model derives from `ScreenViewModel(ScreenServices)`: `Show(NavState)`, `Hide()`, `RefreshIfActive()`,
  abstract `Refresh()`; only the active screen re-projects on store, minute and day events.
  `ScreenServices(AppServices App, TaskActions Actions, IAppCommands Commands, Func<string?> FilterTag)`.
* Rows and cards bind `TaskRowViewModel` (updated in place by `KeyedSync`); `TaskActions` toggles / cycles / moves a
  task, plays the completion sound when enabled and marks a created or moved task for the new-row entrance (one
  `Motion.NewRow`, `EntranceExpiry`).
* Week fits cards from measured row heights (`WeekCardMeasurer` shapes titles with the real font) and shows only the
  lanes that fit (`WeekRowViewModel.VisibleLanes`); Month passes `MonthLayout.Metrics`; Year trims key items to the
  measured cell (`YearRoom`); Today and Day schedules scroll on their own (`ScheduleScroll`).
* Inbox and Today lists are virtualised (per-group `VirtualizingStackPanel` inside one page `ScrollViewer`).
  Recycled rows show their new task's state at once (`SnapScope`).
* `TaskDialogViewModel` wraps Core's `TaskDialog` rules; popovers are built on first open and live in the tree only
  while open (`PopoverAnchor` keeps them 8 px inside the window and no taller than it). `SearchPaletteViewModel` runs
  `Projector.Search` on every keystroke (51 asked, 50 shown, a "More than 50 matches" line).

### 5.5 Theme

* Every colour, radius, font size, spacing and size comes from `Theme/Tokens.axaml`; controls use our own
  `ControlTheme`s (no Fluent / Simple theme). Palette colours, `Color.Chrome.Base` and the `Shadow.*` box shadows live
  in the `Dark` theme dictionary (consume with `DynamicResource`); accent (`Brush.Accent`, `Accent10/15/25`,
  `Color.Accent`) and `Brush.Chrome` are runtime brushes set by `ThemeService` (accent / transparency cross-fade
  300 ms, density table, chrome tint re-applied on theme-variant change).
* **Package regions**: both the Dark dictionary and the theme-independent part end with marked regions (Shell,
  Sidebar, Month, Day, Dialog, TimeWheel, Search, Lists, Calendar). Work adds tokens only inside its region; shared
  tokens are never moved or renamed. Control themes are per area in `Theme/Controls/*.axaml`, merged by
  `Theme/Theme.axaml`.
* **Text rendering**: every top level draws text with grayscale antialiasing and no hinting (`App.Initialize`), like
  the prototype. Fonts: Geist and Geist Mono embedded (`avares://Doba/Assets/Fonts#Geist`), tabular numbers app-wide.
* Pixel-snapping helpers reproduce Chromium where Avalonia rounds differently: `PixelCentring` (glyph centring),
  `TaskCheckBox.RingWidth` (1.5 px ring snapped down to device pixels), `StrikeText` (its own 1-device-px strike line).

### 5.6 Motion

* `Motion`: every duration (ms) and the three curves (`EaseOut`, `EaseIn`, `EaseStd`); `Motion.Ms(x)` is zero when
  `Motion.SystemAnimationsEnabled` is false. `MotionTimes`: the same as `TimeSpan` for `x:Static` in XAML (used inside
  control templates, so each control reads the setting when templated).
* `Animator`: composition (render-thread) animations of opacity, translation and scale with `MotionHandle`
  (`IsAnimating`, `WasCancelled`, `Finished`, `Complete()`, `Cancel()`); a new animation supersedes the old; cancel and
  `Reset` restore opacity 1 and identity. Only opacity and render transforms are ever animated; progress lines animate
  `ScaleX` from the left edge.
* `ContentTransitions` (default `ShellComposition.Transitions`): zoom in / out from the click point or 50 % 40 %,
  period slides, section exit + enter, Settings in / out with the sidebar slide, title fade, launch entrance. No zoom
  blur.
* `SidebarCollapse`: the main panel slides 176 px by a composition translation (340 ms); column widths change once per
  toggle.
* `MotionSync`: when the OS "Animation effects" setting changes while running, zeroes or restores the durations of the
  live windows' XAML transitions (`Motion.SystemAnimationsChanged`).

### 5.7 Platform

```csharp
public sealed partial record PlatformServices(IAutostart Autostart, IGlobalHotkey Hotkey,
    ISystemPreferences Preferences, IShellIntegration Shell, ITrayIcon Tray)
{
    public static PlatformServices Create(PlatformPlan plan);      // Windows parts the plan allows, no-ops otherwise
    public static PlatformServices ForLaunch(bool demo, bool customDataFolder);
    public static PlatformServices NoOp();                          // tests, headless, non-Windows
}
public sealed record PlatformPlan(bool Autostart, bool Hotkey, bool Tray)
{
    // --demo / --selfcheck: none; --data: hotkey + tray, never the Run key; default location: all three.
    public static PlatformPlan For(bool demo, bool customDataFolder);
}
```

* `IAutostart` (HKCU `...\CurrentVersion\Run`, value `Doba` = `"<exe>" --minimized`), `IGlobalHotkey` (Ctrl+Alt+N,
  `RegisterHotKey`), `ISystemPreferences` (OS animation setting, backdrop support, Mica preference, `AnimationsChanged`,
  `ClockChanged` from `WM_TIMECHANGE`), `IShellIntegration` (Explorer for Open folder, `PlaySound` of the synthesized
  click), `ITrayIcon` (Avalonia `TrayIcon`, menu Open Doba / New task / Quit). Preferences and Shell are always the
  platform's own (read-only or click-driven).
* Windows code is in `Platform/Windows/*`, uses `DllImport` with blittable signatures only, and receives `WM_HOTKEY`,
  `WM_SETTINGCHANGE` and `WM_TIMECHANGE` on Doba's own hidden `PlatformWindow`.
* `SelfCheck` (`--selfcheck`): nine checks on a demo window (HANDOFF §2). `StartupError`: stderr + native message box.

### 5.8 Services

* `AppLifecycle`: tray and close-to-tray, `--minimized` start, hotkey registration after `Opened`, after the first
  frame: `DataMaintenance.Start`, autostart reconcile (default location only), tray icon; flush on hide-to-tray, Quit,
  Exit and session end (3 s bound); `ShowWindow`, `QuickAdd`; `ClockChanged` → `TimeZoneInfo.ClearCachedData()` +
  `DayClock.Check()`.
* `DataMaintenance`: carry-over (UI thread, one `Batch`) and daily backup (background, serialised, failures to Trace)
  after the first frame and on every `DayChanged`; never in demo.
* `DayClock`, `KeyedSync`, `TaskActions`, `SettingsService`, `SingleInstance` (per-session named mutex
  `Local\Doba-<hash of the case-folded full path>` + a named "show" event), `DataLog` (`doba.log`, 256 KB).

### 5.9 Diagnostics

`PerfProbe` (startup marks, one branch when off), `PerfSession` (prints the `doba-perf …` lines and ends the app for
the perf flags), `WeekScenario` and `OverlayScenario` (driven navigations / dialog cycles with `FrameMeter` statistics
from render-thread and UI-thread frame callbacks, GC attribution in `LongFrames`). Frame times are proxies, not GPU
present times.

## 6. Command line

Parsed by `StartupFlags.TryParse` (case-insensitive, strict). Any unknown argument, a value flag without a value, a
value that is itself a flag, an empty `--data` or an out-of-range number is an error: stderr + usage, exit 2.

| Flag | Effect |
|---|---|
| `--demo` | DemoData + fixed clock; no disk; `PlatformPlan` none (no autostart, hotkey or tray). |
| `--data <folder>` | That data folder; claims it; hotkey and tray yes, Run key never. |
| `--minimized` | Start hidden in the tray (tray setting on and available), otherwise minimised. Passed by the Run value. |
| `--selfcheck` | Platform self-check on a demo window; exit 0 / 1. Wins over `--demo` and `--data`. |
| `--perf` | `doba-perf` lines: time to first frame, marks, memory. |
| `--exit-after-first-frame` | Quit once the first frame with data is rendered. |
| `--perf-idle-seconds N` | 1–600, implies `--perf`: idle sample after N s, then quit. |
| `--perf-scenario week` | Implies `--perf`: Week navigation scenario, then the overlay scenario; quit. |
| `--perf-rounds N` | 1–100: rounds of the scenario. |

Exit codes: 0 normal or handed over to the running instance, 1 self-check failure, 2 bad command line, 3 the data
folder could not be opened (`StartupFailedException`; nothing in the folder is changed).

## 7. Testing and verification

* Gates: `dotnet build -warnaserror` and `dotnet test` (Microsoft Testing Platform per `global.json`; xunit.v3 3.2.2,
  pinned because `Avalonia.Headless.XUnit` 12.1.6 is built against it). NativeAOT publish with `-warnaserror` must stay
  at 0 warnings.
* **Doba.Core.Tests**: store, invariants, carry-over, date math, navigator, dialog rules, projections, and golden tests
  (`Golden/`): `generate.js` runs the prototype's own script under Node and writes the committed fixtures; the tests
  compare Core's records with them (Node is not needed to run them). `PerformanceTests`: every projection on 10,000
  tasks, average under 20 ms (5 ms × 4 margin), one-character Search median under 5 ms and under 32 KB per call.
* **Doba.Storage.Tests**: database round trips, migrations, writer bursts and failures (a parked writer forces one
  burst), backup rotation, JSON import / export validation, load / persist timing.
* **Doba.App.Tests**: headless UI with real Skia rendering. `Support/ShellFixture` opens the real `MainWindow` on
  `AppServices.CreateDemo(settings, PlatformServices.NoOp())` (never a plan that allows autostart, hotkey or tray) and
  drives keys, clicks and render ticks. Folders: Controls, DesignSystem, Dialogs, Keyboard, Lifecycle (on fake platform
  services), Motion, Performance, Platform, Screens, Services, Shell.
* **Screenshots**: tests render 1600×1000 with DemoData (plus a few 1100×700 and content-only renders) into the
  git-ignored `artifacts/screenshots/`; `DOBA_SCREENSHOTS=1` writes the tracked `docs/screenshots/` instead. Reference
  renders of the prototype are in `docs/design/screens/` (made with `tools/design-render`).
* **No-literals gate** (`DesignSystem/NoLiteralsTests`): every `.axaml` and `.cs` under `src/Doba.App` except
  `Theme/Tokens.axaml`, `Theme/AccentPalette.cs`, `Motion/Motion.cs`, `Motion/MotionTimes.cs` and `Platform/` is
  scanned for colour literals (hex, named colours in attributes and setters, colour factories and constructors),
  durations (`TimeSpan.From…(<number>)`, clock strings, `Duration` / `Delay` attributes), easings outside `Motion`
  (`Easing="…"`, `new …Easing(`, `KeySpline`) and `Task.Delay(<number>)`. Markup under `Screens/` and `Controls/` is
  also scanned for numeric layout values (margins, paddings, spacing, sizes, radii, font sizes, line heights, strokes,
  grid definitions); only 0, ±1, star weights and `Auto` are allowed. A two-entry allowlist covers `DayClock`'s timer
  arithmetic.
* **Performance**: `scripts/perf.ps1` publishes the AOT build, seeds databases through the Storage API
  (`PerfDatabaseTests` with `DOBA_PERF_SEED_DIR`), and measures warm startup, a partly cold start, idle memory and the
  Week / overlay scenarios; `-PrepareCold` / `-Cold` measure a start after a reboot. Results: HANDOFF §4.
