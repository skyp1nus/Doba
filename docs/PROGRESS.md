# Progress

The state of the project. Tick an item only when its gates pass: `dotnet build -warnaserror` clean (incl. trim/AOT
analyzers), `dotnet test` green, and for UI work screenshots regenerated at 1600×1000 with DemoData and accepted by a
fresh reviewer agent, with no literal colours/durations outside tokens and `Motion`.

## Phase 0 — Bootstrap

- [x] **P0 Bootstrap** — SDK, design read, ARCHITECTURE/PROGRESS/DECISIONS/CLAUDE.md, solution scaffold with analyzers + central versions + CI, headless screenshot test, Core/Storage/App contracts, DemoData contract.
  - Also done by the lead: `KeyedSync` (row reconciliation), `DayClock` (minute tick + midnight), `--perf/--exit-after-first-frame/--demo/--data` flags.
  - Notes: SDK 11.0.100 from packages.microsoft.com (Debian 12 feed). AOT publish (linux-x64) of the scaffold: 0 warnings.
  - Resumed 2026-11-18 on the owner's Windows 11 PC (the cloud VM was reclaimed; Shell, Month, Day and dialog work in flight there was lost). Windows baseline of `dev`: `dotnet build -warnaserror` 0 warnings, 417 tests green (20 s), first NativeAOT `win-x64` publish 0 warnings (Doba.exe 12.9 MB, `e_sqlite3.dll` beside it), placeholder window `--perf`: 939 ms first run, ~95 ms warm, 53 MB. Screenshots re-rendered (glyph fallback differs from Linux).
  - Contracts for the remaining packages added by the lead: `Shell/ShellContracts.cs` (`IShellNavigation`, `IContentTransitions`), `Shell/OverlayViewModel.cs`, `Shell/ShellComposition.cs`, compiling stubs (Month, Day, Sidebar, TaskDialog, SearchPalette, TimeWheel), per-package theme files and token regions, opt-in screenshot writing (`DOBA_SCREENSHOTS=1`).

## Phase 1 — Foundations

- [x] **P1.1 Core** — model, `Tags.Normalize`, `DateMath`, `TaskStore` (indexes, events, persistence calls), `Projector` (Inbox, Today, Day, Week, Month, Year, Sidebar, Search), `ScheduleLayout`, `Navigator` + `HeaderProjection`, `TaskDialog` rules, `DemoData` (exact seed incl. mulberry32 fill).
  - Notes: 190 tests; golden fixtures generated from the prototype's own JS (`tests/Doba.Core.Tests/Golden/generate.js`): 348 seed tasks, 44 screen cases, 41 dialog cases, 4 navigation sequences — all match. Perf on 10k tasks (avg, Debug): Inbox 1.34 ms, Today 1.08, Day 0.008, Week 0.025, Month 0.065, Year 0.16, Sidebar 0.32, Search 0.84.
  - Accept: golden tests vs the prototype JS run under Node for all six screen projections + header + dialog labels; unit tests for date math (ISO weeks, Sunday start), store events, navigation; perf test: each projection < 5 ms avg on 10,000 tasks (CI margin ×4).
- [x] **P1.2 Storage** — `DobaDatabase` (schema v1, WAL, user_version migrations, write-behind channel, flush on dispose), settings persistence, `Backup.RunDailyAsync` (keep 7), `JsonTransfer` (source-generated).
  - Notes: 88 tests. Write-behind channel, burst transactions (≤4096 ops), per-op replay on failure, errors surface from FlushAsync. Load 10k ≈ 15–22 ms; persist 10k ≈ 75–112 ms (AOT 47–80 ms). AOT smoke publish of a Storage console app: 0 warnings. Ships e_sqlite3 native lib beside the exe.
  - Accept: round-trip tests (tasks/tags/settings), crash-safety (dispose flushes), migration from empty, backup rotation, JSON export→import equality, invalid JSON → FormatException; AOT/trim analyzers clean.
- [x] **P1.3 Design system** — `Theme/Tokens.axaml`, fonts, ControlThemes (buttons: primary/outline/ghost/icon, checkbox, priority icon, segmented switch with sliding indicator, toggle switch, pill, kbd, text box, scrollbars, menu item), accent as dynamic resources, density resources, `Motion` animator helpers, literal-colour/duration grep test, a controls gallery screenshot.
  - Notes: tokens in `Theme/Tokens.axaml` (Dark ThemeDictionary, Light-ready), own ControlThemes (no Fluent/Simple), controls (TaskCheckBox, PriorityIcon, SegmentedSwitch, ToggleSwitch, Pill, Kbd, ProgressLine, Card, FocusRing), composition-based `Animator` with cancel/reset, `ThemeService` (accent/density/chrome tint). Gallery `docs/screenshots/controls-gallery.png` compared with the prototype renders by the lead and the implementer; `docs/design-system.md` documents usage. NoLiterals test has a 2-entry allowlist for DayClock's timer arithmetic.
  - Accept: gallery screenshot reviewed against the prototype's control styles; grep test passes.
- [x] **P1.4a Shell core** — `App.axaml.cs` builds `AppServices` and the window through `ShellComposition.CreateMainWindow`; `MainWindow` custom chrome (40 px title bar, app mark + "Doba", search button, caption buttons with element roles), page header (crumbs, title+subtitle, Year/Month/Week/Day switch with sliding indicator, Today, ‹ ›, New task), `MainViewModel` (`IAppCommands` + `IShellNavigation`, `NavState`), `ScreenHost` + `ScreenFactory` (views created on first visit and cached), `OverlayHost` (shade, Esc routing, focus return), `DayClock.Start()`, `ThemeService.Apply` at startup, `ShellFixture` screenshot harness.
  - Accept: VM tests for every Navigator path; header strings equal `HeaderProjection`; one active screen at a time; filter/accent/density/week start apply live; overlay open/close + Esc + focus return with a fake overlay; full-window screenshot tests for every design name; renders at 1100×700 without layout errors; reviewed against `docs/design/screens/`.
  - Notes (2026-11-18): implemented in a worktree, fresh code review accepted, then a visual review of every full-window render against `docs/design/screens` (pixel-measured) and a second fresh acceptance pass after the fixes. 698 tests green. Chrome accepted on `week`, `sidebar-collapsed` and `settings-appearance` (title bar, header and sidebar on the reference pixels; backdrop within 3 colour levels).
- [x] **P1.4b Sidebar** — Inbox/Today/Calendar with open counts and the sliding highlight, Tags with inline new-tag input + filter, Collapse → 56 px rail, Settings; settings sidebar (Back to app, six tabs, footer "Doba 0.1 · local only").
  - Accept: counts follow the store; highlight follows the section and hides in Settings; tag add normalises and rejects duplicates; filter toggles; rail layout; reviewed against the design.

## Phase 2 — Screens

- [x] **P2.1 Inbox** _(accepted 2026-11-18: `inbox.png` matches the reference; virtualised: 5k undated → <200 realised rows; Space completes the focused row)_ — two columns, groups, rows (checkbox, priority, title, tag, meta), Add task row, virtualised list. Screenshot `inbox.png`.
- [ ] **P2.2 Today** _(wired and reviewed; not yet accepted: done titles in list rows and schedule blocks still use the fractional strike, and the time range of a truncated block is 3 px left — in the polish pass)_ — summary + progress, groups incl. Overdue with "→ Today", In progress, Scheduled, Anytime, Done; schedule timeline with now-line. Screenshot `today.png`.
- [x] **P2.3 Week** _(accepted 2026-11-18: `week.png` track geometry, bars, cards and "+N more" on the reference pixels; titles break between words at 1100×700)_ — two rows of day cells, lanes of multi-day bars, cards, "+N more" from measured size, next-week preview, per-day progress. Screenshot `week.png`.
- [x] **P2.4 Month** _(accepted 2026-11-18: week rules, columns, bars, chips and "+N more" identical to the reference geometry; capacity follows the measured height)_ — weekday header, week rows with W-numbers, day cells, span bars (2 lanes), "+N more". Screenshot `month.png`.
- [x] **P2.5 Year** _(accepted 2026-11-18: `year.png` track edges and header stacks match the reference)_ — 4×3 months with stats, progress, key items, "+N more". Screenshot `year.png`.
- [ ] **P2.6 Day** _(implemented and reviewed; not yet accepted: schedule strike and truncated-block range, shared with Today — in the polish pass)_ — spans, task cards with meta, Add task, timeline. Screenshot `day.png`.
- [x] **P2.7a Time wheel** _(accepted 2026-11-18: 652-line test suite incl. settle commit, snap, clamp, keys, animations-off; wheel area matches `dialog-time.png` pixel for pixel)_ — `Controls/TimeWheel`: 34 px rows, 5 visible, snap to centre, selection band, fade mask, commit after 120 ms of stillness, click scrolls smoothly, instant positioning on open.
- [x] **P2.7b Task dialog** _(accepted 2026-11-18: fresh review accepted; `dialog-date|time|priority|tag.png` match the references on panel, pills, popovers, calendar cells and wheel; leftovers are 1–3 px text-width rounding)_ — title, notes, Date/Time/Priority/Tag pills and popovers (mini calendar, quick options, lasts N days; time wheel, duration; priority list; tag list), Enter/Esc/Delete rules, Create disabled until a title. Screenshots `dialog-date.png`, `dialog-time.png`, `dialog-priority.png`, `dialog-tag.png`.
- [x] **P2.8 Settings** _(accepted 2026-11-18: all six `settings-*.png` accepted; column 261–1040, switches pixel-identical; export/import round trip tested)_ — settings sidebar, six tabs with live controls bound to `ISettingsService`. Screenshots `settings-*.png` (6).
- [x] **P2.9 Density + rail** _(accepted 2026-11-18: `density-compact.png` and `sidebar-collapsed.png` accepted; compact shows as many cards as fit, by decision)_ — compact density across screens; collapsed sidebar rail. Screenshots `density-compact.png`, `sidebar-collapsed.png`.

## Phase 3 — Motion

- [ ] **P3.1 Transitions** — zoom in/out from click point, period slides, section exit+enter with direction, settings in/out (+ sidebar slide), title fade, launch, sidebar collapse; cancellation on rapid clicks.
- [ ] **P3.2 Micro-interactions and overlays** — checkbox/strike/dim, new-row entrance, tag-filter dim, progress scale, toggles, segments, hovers, dialog open/close (focus return), popovers, wheel text, OS animations-off.
- [ ] **P3.3 Motion acceptance** — every item of the motion-spec checklist covered by a test or recorded review (`docs/MOTION-REVIEW.md`).

## Phase 4 — Platform and remaining features

- [x] **P4.1 Windows platform** _(done 2026-11-18: `AppLifecycle` wires tray, close-to-tray, hotkey, autostart reconcile, Mica/acrylic, OS animations; fresh review accepted; `Doba.exe --selfcheck` on the NativeAOT build: 9/9 checks pass on Windows 11; what only a person can check is listed under "Needs Windows verification")_ — tray (keep running in tray), autostart (HKCU Run), global hotkey Ctrl+Alt+N, backdrop transparency (Mica/acrylic + tint from the transparency setting), OS animation setting, open folder, completion sound; no-op fallbacks.
- [x] **P4.2 Data features** _(done 2026-11-18: flush on hide/quit/exit/session end, carry-over and daily backup after the first frame and on day change, settings survive a restart — all tested; a real AOT run on a temp data folder moved an overdue task to today and wrote the day's backup)_ — daily backup wiring, Export / Import (file pickers), Open folder, settings persistence wiring, carry-over at startup and midnight, midnight rollover.
- [ ] **P4.3 Search palette** _(implemented; review found the panel ring/shadow clipped — fixed, re-check pending in the polish pass)_ — Ctrl K overlay in the dialog style, substring search, arrows + Enter opens the task. Screenshot `search.png`.
- [ ] **P4.4 Keyboard** — N, Ctrl K, G I, G T, 1–4, ← →, Space on focused task, Esc; Tab order with visible focus everywhere.

## Phase 5 — Hardening

- [ ] **P5.1 Performance** — `--perf` time-to-first-frame log, idle working-set measurement, Week-view frame budget check, Inbox virtualisation verified, numbers recorded in HANDOFF.
- [ ] **P5.2 AOT** — `dotnet publish -r win-x64` (CI) and `-r linux-x64` (local) with zero trim/AOT warnings.
- [ ] **P5.3 CI** — GitHub Actions on `windows-latest`: build, test, publish NativeAOT win-x64, upload artifact, print perf; green. Owner's instruction (2026-11-18): no extra Actions runs — everything is verified locally on Windows; the only run is the one the final PR into `main` starts.
- [ ] **P5.4 Independent review** — fresh reviewer agents: code review of the whole solution and a final visual review of all screenshots; findings fixed.
- [ ] **P5.5 Docs** — README (what, build, run, publish, shortcuts, data location), HANDOFF (verified how, Needs Windows verification with steps, perf numbers, gaps), pull request / branch summary.

## Blocked

(none)

## Needs Windows verification

Verified here by `Doba.exe --selfcheck` (NativeAOT, Windows 11): window + icon, Mica actually active, OS animation setting read, hidden message window, Ctrl+Alt+N register/unregister with a simulated press, tray icon create/remove, autostart write/read/remove against a test registry key.

Still needs a person (exact steps go to HANDOFF.md):

- Tray: close hides the window, left-click restores, menu Open Doba / New task / Quit, tray setting off → close quits.
- Global hotkey Ctrl+Alt+N from another app, with Doba hidden in the tray and minimised.
- Launch at startup: the real HKCU Run value on the default data location, and an actual start after sign-in (hidden in the tray).
- Mica / acrylic look under the chrome tint and the transparency slider (acrylic on Windows 10 untested: this PC is Windows 11).
- OS "Animation effects" switched off while Doba runs: everything becomes instant.
- Session end: add a task, sign out at once, the task is there after sign-in.
- Sleep through midnight: date rolls over, carry-over and backup happen.
- Title bar on a real window: drag, double-click maximise, snap-layout flyout on Maximise, resize borders, mixed-DPI monitors.
