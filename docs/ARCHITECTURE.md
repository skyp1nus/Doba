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
App.axaml(.cs)          Application; builds AppServices (composition root) and calls ShellComposition.CreateMainWindow (Shell package).
AppServices.cs          Hand-written composition root: clock, store, database, settings, platform, DayClock.
Program.cs              Entry point; flags: --demo (DemoData + fixed clock, no disk), --data <folder>, --perf.
MainWindow.axaml(.cs)   The window (custom chrome); its content is composed by the Shell package.
Theme/                  Tokens.axaml (colours, brushes, radii, font sizes, spacing), Icons, Styles, Controls/*.axaml (ControlThemes).
Motion/                 Motion (tokens), MotionTimes, Animator (composition animation helpers, MotionHandle).
Platform/               IAutostart, IGlobalHotkey, ISystemPreferences, IShellIntegration; Windows/ implementations; no-op fallbacks.
Services/               ISettingsService, DayClock, KeyedSync, TaskActions (toggle/cycle/move, completion sound).
Shell/                  IAppCommands, ShellContracts (IShellNavigation, IContentTransitions), OverlayViewModel, ShellComposition,
                        SidebarViewModel + SidebarView / SettingsSidebarView; Shell package: MainViewModel, TitleBar,
                        PageHeader, ScreenHost, ScreenFactory, OverlayHost.
Screens/<Name>/         <Name>View.axaml + <Name>ViewModel.cs for Inbox, Today, Week, Month, Year, Day, Settings.
Dialogs/                TaskDialogViewModel + TaskDialogView (+ Date/Time/Priority/Tag popovers); Search/SearchPalette*.
Controls/               Reusable: TaskCheckBox, PriorityIcon, ProgressLine, Kbd, SegmentedSwitch, ToggleSwitch, Pill, Card,
                        TaskCard, TaskListRow, TaskGroupList, ScheduleTimeline, AddTaskButton, SpanBarButton, TimeWheel.
```

* **MVVM**: CommunityToolkit.Mvvm source generators (`[ObservableProperty]` on partial properties, `[RelayCommand]`);
  compiled bindings everywhere (`x:DataType` on every view); no reflection binding, no DI container.
* **Theme tokens**: every colour, radius, font size and spacing comes from `Theme/Tokens.axaml` (`Color.*`, `Brush.*`,
  `Radius.*`, `FontSize.*`); controls use our own `ControlTheme`s (no Fluent theme). Accent is a dynamic resource
  (`Brush.Accent`, `Brush.Accent10`, `Brush.Accent15`, `Brush.Accent25`) updated from settings.
  Fonts: Geist and Geist Mono embedded (`avares://Doba/Assets/Fonts#Geist`).
* **Package regions**: Tokens.axaml ends with one marked region per work package (`<!-- == Shell == -->`, Sidebar,
  Month, Day, Dialog, TimeWheel, Search), once in the Dark palette dictionary and once in the
  theme-independent dictionary. A package adds tokens only inside its own region and never moves or renames shared
  tokens. Each package's ControlThemes go in its own `Theme/Controls/<Package>.axaml` (Shell, Sidebar, Dialog, Search,
  TimeWheel), all merged by `Theme/Theme.axaml`.
* **Motion**: every duration/easing comes from `Doba.App.Motion.Motion`. Content transitions animate only opacity
  and render transforms (translate/scale) — on the composition visual where possible — never Margin/Width/layout;
  progress bars animate a `ScaleTransform.ScaleX` (origin left). The zoom blur is optional (dropped if costly).
  `Motion.SystemAnimationsEnabled` mirrors the OS setting; when false every duration is 0.
* **Platform**: Windows-only features live in `Platform/Windows/*` behind the interfaces, use `DllImport` with
  blittable signatures only (no marshalling stubs under NativeAOT; see DECISIONS) and are selected at runtime with
  `OperatingSystem.IsWindows()`. Headless/Linux and tests use the no-op fallbacks (`PlatformServices.NoOp()`).
* **Window chrome**: `ExtendClientAreaToDecorationsHint`, our own 40 px title bar; caption buttons and the drag area
  are marked with `WindowDecorationProperties.SetElementRole` (Avalonia 12) so Windows snap layouts/hit-testing work.

### 5.1 Shell contracts

These names are fixed so the Shell, Sidebar, Month, Day, TimeWheel, Task dialog, Search, Lifecycle and Motion packages
compile against each other from day one; each package edits only its own files. The contracts live in
`Doba.App.Shell` (`Shell/ShellContracts.cs`, `Shell/OverlayViewModel.cs`, `Shell/ShellComposition.cs`) and every
member's XML doc states the rule it carries.

```csharp
// Shell/ShellContracts.cs. Implemented by MainViewModel; used by the sidebar, page header and keyboard handler.
// All calls on the UI thread; implementations apply Navigator (Core) results and never invent navigation rules.
public interface IShellNavigation
{
    NavState Nav { get; }
    void GoSection(AppView view);          // Navigator.GoSection (anchor: today for Today)
    void GoCalendar();                     // Navigator.GoCalendar
    void SetZoomLevel(AppView level);      // header segments: Navigator.Zoom at the current anchor, no origin
    void Shift(int direction);             // ‹ / ›, arrows: Navigator.Shift (−1 / +1)
    void GoToday();                        // Navigator.GoToday
    void OpenSettings();
    void BackFromSettings();
    void SetSettingsTab(SettingsTab tab);
    bool IsSidebarCollapsed { get; }       // in-memory only, never persisted
    void ToggleSidebar();
    string? FilterTag { get; }
    void ToggleTagFilter(string tag);      // the same tag again clears the filter
    string? AddTag(string raw);            // normalised name, or null if rejected (empty / duplicate)
    void OpenSearch();
    event EventHandler<NavigatedEventArgs>? Navigated;   // exactly once per step whose kind is not None
}

public sealed class NavigatedEventArgs : EventArgs
{
    public NavigatedEventArgs(NavState from, NavState to, TransitionKind kind, Point? origin);
    public NavState From { get; }
    public NavState To { get; }
    public TransitionKind Kind { get; }
    public Point? Origin { get; }          // clicked element centre as 0..1 of the content area; null = no click
}

public sealed record ContentTransitionTargets(Control Content, Control Title, Control? AppSidebar, Control? SettingsSidebar);

public interface IContentTransitions      // implemented by the Motion package
{
    void Run(TransitionKind kind, Point? origin, Action swap, ContentTransitionTargets targets);
    void Launch(ContentTransitionTargets targets);
}

public sealed class InstantContentTransitions : IContentTransitions;   // Run calls swap() at once; Launch does nothing

// Shell/OverlayViewModel.cs
public abstract partial class OverlayViewModel : ObservableObject
{
    public event EventHandler? CloseRequested;
    protected void RequestClose();
    public virtual bool OnEscape() => false;   // true = handled inside (e.g. closed a popover); false = host closes
    public virtual void OnClosed() { }         // after the exit animation; the task dialog commits its save here
}

public interface IOverlayView { void FocusInitial(); }   // implemented by overlay views

// Shell/ShellComposition.cs
public static class ShellComposition
{
    public static IContentTransitions Transitions { get; set; } = new InstantContentTransitions();
    public static MainWindow CreateMainWindow(AppServices app, Application application);   // placeholder: new MainWindow()
}
```

`IContentTransitions` rules: `Run` invokes `swap` exactly once — for `SectionDown`/`SectionUp` only after the exit
phase, for every other kind before the enter animation; a new `Run` cancels the running one (a still-pending swap is
invoked at once, so swaps are never lost or reordered); content is never left invisible (cancel, completion and
animations-off all end at Opacity 1 with an identity render transform).

Placeholder surfaces (compiling stubs; their packages fill them in without changing these signatures):

```csharp
public sealed partial class MonthViewModel(ScreenServices services) : ScreenViewModel(services);  // Screens/Month/MonthView
public sealed partial class DayViewModel(ScreenServices services) : ScreenViewModel(services);    // Screens/Day/DayView
public sealed partial class SidebarViewModel : ObservableObject         // Shell/SidebarView, Shell/SettingsSidebarView
{
    public SidebarViewModel(AppServices app, IShellNavigation shell);
    public void Refresh();
}
public sealed partial class TaskDialogViewModel : OverlayViewModel      // Dialogs/TaskDialogView : IOverlayView
{
    public TaskDialogViewModel(ScreenServices services, TaskDraft draft);
    [ObservableProperty] public partial TaskDraft Draft { get; set; }
}
public sealed partial class SearchPaletteViewModel : OverlayViewModel   // Dialogs/Search/SearchPaletteView : IOverlayView
{
    public SearchPaletteViewModel(ScreenServices services);
}
public class TimeWheel : TemplatedControl                               // Controls/TimeWheel.cs, Theme/Controls/TimeWheel.axaml
{
    public IReadOnlyList<string> Items { get; set; }      // StyledProperty, default empty
    public int SelectedIndex { get; set; }                // StyledProperty, TwoWay by default
    public event EventHandler<int>? Committed;            // once when the wheel settles after user input
    public void ScrollToIndex(int index, bool animate);   // sets SelectedIndex; never raises Committed
}
```

**Implemented by the Shell package** (not in the code yet; other packages may already rely on these names):

```csharp
namespace Doba.App.Shell;

public sealed partial class MainViewModel : ObservableObject, IAppCommands, IShellNavigation, IDisposable
{
    public MainViewModel(AppServices app, ThemeService theme, NavState? start = null);
    public static NavState InitialState(AppSettings s, DateOnly today);   // LaunchView → Today / Week / Inbox at today
    public AppServices App { get; }
    public ScreenServices Screens { get; }          // Commands = this, FilterTag = () => FilterTag
    public SidebarViewModel Sidebar { get; }
    public PageHeaderViewModel Header { get; }
    // Observable: Nav (NavState), Current (ScreenViewModel), FilterTag (string?), Overlay (OverlayViewModel?),
    // IsSidebarCollapsed (bool; in-memory, not persisted).
    public ScreenViewModel ScreenFor(AppView view); // lazy, one cached instance per view
    public void OpenOverlay(OverlayViewModel overlay);
    public void CloseOverlay();                     // called by OverlayHost after the exit animation; then OnClosed()
}
```

```csharp
// tests/Doba.App.Tests/Support/ShellFixture.cs: the full-window test harness.
public sealed class ShellFixture : IDisposable
{
    public static ShellFixture Open(AppView view = AppView.Week, DateOnly? anchor = null, AppSettings? settings = null,
        SettingsTab tab = SettingsTab.Appearance, double width = 1600, double height = 1000);
    public AppServices App { get; }   // always AppServices.CreateDemo(settings, PlatformServices.NoOp()), never PlatformServices.Create()
    public MainViewModel Vm { get; }
    public MainWindow Window { get; }
    public T Screen<T>() where T : ScreenViewModel;
    public void Navigate(AppView view, DateOnly? anchor = null);
    public void Key(Key key, RawInputModifiers mods = RawInputModifiers.None);
    public void Click(Control target);
    public void Settle();             // run dispatcher jobs, layout, force render ticks, complete running Animator handles
    public string Capture(string name);   // Settle, then Screenshots.Capture
    public void Dispose();            // restores Motion.SystemAnimationsEnabled
}
```

Shell rules:

* **Overlays** (task dialog, its Date/Time/Priority/Tag popovers, search palette) are in-window panels hosted by
  `Shell/OverlayHost`, never Avalonia `Popup` windows: headless frame capture does not include popup top-levels. The
  host owns focus (`IOverlayView.FocusInitial`), Esc routing (`OnEscape()` first; `false` closes) and the exit
  animation, after which `CloseOverlay()` runs `OnClosed()`.
* **ScreenHost** creates each screen view on its first visit and caches it; switching back reuses the view.
* **ScreenFactory** maps `AppView` → view-model and view-model → view with `switch` expressions (no reflection, no
  type-name lookup), so NativeAOT sees every type.
* **Navigation** goes only through `IShellNavigation` / `IAppCommands`. `MainViewModel` applies `Navigator` results
  and raises `Navigated`; the window plays `ShellComposition.Transitions.Run(...)` around the screen swap.

## 6. Testing and verification

* `dotnet build -warnaserror` (trim + AOT analyzers on for shipping projects) and `dotnet test` (Microsoft Testing
  Platform; xunit v3 3.2.x — see DECISIONS).
* Core golden tests compare `Projector` output with the prototype's own JS functions executed under Node on the same
  seed (`tests/Doba.Core.Tests/Golden/`).
* Screenshots: `tests/Doba.App.Tests` renders the real `MainWindow` headlessly with Skia at 1600×1000 using
  `DemoData` (`--demo` equivalent). PNGs go to the git-ignored `artifacts/screenshots/` by default; set
  `DOBA_SCREENSHOTS=1` to refresh the committed `docs/screenshots/<name>.png` (so test runs in parallel worktrees never
  rewrite tracked binaries). Reference images of the prototype are in `docs/design/screens/`.
* A grep-based test fails on literal colours (`#RRGGBB`) or durations outside `Theme/` and `Motion/`.
* Performance: projection perf test (10k tasks, < 5 ms each with CI margin), `--perf` logs time-to-first-frame,
  CI prints it.
