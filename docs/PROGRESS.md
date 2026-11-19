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
- [x] **P2.2 Today** _(accepted 2026-11-18 after the polish pass: crisp 1 px strikes in rows and schedule blocks, truncated block range flush right; fresh acceptance review found nothing remaining)_ — summary + progress, groups incl. Overdue with "→ Today", In progress, Scheduled, Anytime, Done; schedule timeline with now-line. Screenshot `today.png`.
- [x] **P2.3 Week** _(accepted 2026-11-18: `week.png` track geometry, bars, cards and "+N more" on the reference pixels; titles break between words at 1100×700)_ — two rows of day cells, lanes of multi-day bars, cards, "+N more" from measured size, next-week preview, per-day progress. Screenshot `week.png`.
- [x] **P2.4 Month** _(accepted 2026-11-18: week rules, columns, bars, chips and "+N more" identical to the reference geometry; capacity follows the measured height)_ — weekday header, week rows with W-numbers, day cells, span bars (2 lanes), "+N more". Screenshot `month.png`.
- [x] **P2.5 Year** _(accepted 2026-11-18: `year.png` track edges and header stacks match the reference)_ — 4×3 months with stats, progress, key items, "+N more". Screenshot `year.png`.
- [x] **P2.6 Day** _(accepted 2026-11-18 after the polish pass: header, cards, spans and schedule on the reference rows; nothing remaining)_ — spans, task cards with meta, Add task, timeline. Screenshot `day.png`.
- [x] **P2.7a Time wheel** _(accepted 2026-11-18: 652-line test suite incl. settle commit, snap, clamp, keys, animations-off; wheel area matches `dialog-time.png` pixel for pixel)_ — `Controls/TimeWheel`: 34 px rows, 5 visible, snap to centre, selection band, fade mask, commit after 120 ms of stillness, click scrolls smoothly, instant positioning on open.
- [x] **P2.7b Task dialog** _(accepted 2026-11-18: fresh review accepted; `dialog-date|time|priority|tag.png` match the references on panel, pills, popovers, calendar cells and wheel; leftovers are 1–3 px text-width rounding)_ — title, notes, Date/Time/Priority/Tag pills and popovers (mini calendar, quick options, lasts N days; time wheel, duration; priority list; tag list), Enter/Esc/Delete rules, Create disabled until a title. Screenshots `dialog-date.png`, `dialog-time.png`, `dialog-priority.png`, `dialog-tag.png`.
- [x] **P2.8 Settings** _(accepted 2026-11-18: all six `settings-*.png` accepted; column 261–1040, switches pixel-identical; export/import round trip tested)_ — settings sidebar, six tabs with live controls bound to `ISettingsService`. Screenshots `settings-*.png` (6).
- [x] **P2.9 Density + rail** _(accepted 2026-11-18: `density-compact.png` and `sidebar-collapsed.png` accepted; compact shows as many cards as fit, by decision)_ — compact density across screens; collapsed sidebar rail. Screenshots `density-compact.png`, `sidebar-collapsed.png`.

## Phase 3 — Motion

- [x] **P3.1 Transitions** _(accepted 2026-11-19: `Motion/ContentTransitions` + `SidebarCollapse` + `MotionSync`; the independent motion review audited every row against the spec with recorded frame strips; its findings (highlight in sync with the section entrance, content clip) are fixed)_ — zoom in/out from click point, period slides, section exit+enter with direction, settings in/out (+ sidebar slide), title fade, launch, sidebar collapse; cancellation on rapid clicks.
- [x] **P3.2 Micro-interactions and overlays** _(accepted 2026-11-19: every table row audited and tested, overlay open/close motion with the backdrop blur, focus returns after the write with the ring for keyboard openers; measured on the real GPU window)_ — checkbox/strike/dim, new-row entrance, tag-filter dim, progress scale, toggles, segments, hovers, dialog open/close (focus return), popovers, wheel text, OS animations-off.
- [x] **P3.3 Motion acceptance** _(done 2026-11-19: `docs/MOTION-REVIEW.md` maps the six checklist items and every spec row to tests and recorded observations, and lists what only a person can judge on a real display)_ — every item of the motion-spec checklist covered by a test or recorded review (`docs/MOTION-REVIEW.md`).

## Phase 4 — Platform and remaining features

- [x] **P4.1 Windows platform** _(done 2026-11-18: `AppLifecycle` wires tray, close-to-tray, hotkey, autostart reconcile, Mica/acrylic, OS animations; fresh review accepted; `Doba.exe --selfcheck` on the NativeAOT build: 9/9 checks pass on Windows 11; what only a person can check is listed under "Needs Windows verification")_ — tray (keep running in tray), autostart (HKCU Run), global hotkey Ctrl+Alt+N, backdrop transparency (Mica/acrylic + tint from the transparency setting), OS animation setting, open folder, completion sound; no-op fallbacks.
- [x] **P4.2 Data features** _(done 2026-11-18: flush on hide/quit/exit/session end, carry-over and daily backup after the first frame and on day change, settings survive a restart — all tested; a real AOT run on a temp data folder moved an overdue task to today and wrote the day's backup)_ — daily backup wiring, Export / Import (file pickers), Open folder, settings persistence wiring, carry-over at startup and midnight, midnight rollover.
- [x] **P4.3 Search palette** _(accepted 2026-11-18: ring and shadow on all four sides, dialog-style rows and footer; `search.png` accepted against the dialog's visual language)_ — Ctrl K overlay in the dialog style, substring search, arrows + Enter opens the task. Screenshot `search.png`.
- [x] **P4.4 Keyboard** _(accepted 2026-11-18: fresh review accepted; every shortcut tested through real headless key events incl. chord timing and the Ukrainian layout mapping; Tab walk on every screen and overlay with a visible focus ring)_ — N, Ctrl K, G I, G T, 1–4, ← →, Space on focused task, Esc; Tab order with visible focus everywhere.

## Phase 5 — Hardening

- [x] **P5.1 Performance** _(done 2026-11-19: `scripts/perf.ps1` on the final NativeAOT build, quiet machine — warm first frame 216–233 ms from process start (demo, 348 and 10,000 tasks; budget 400 ms; true cold start not measured), idle working set 107 / 110 / 122 MB (10,000 tasks is 2 MB over the 120 MB aim), Week scenario render-thread p95 5.6 ms with no frame over 33 ms at 38 cards + 5 bars, UI-thread pauses of 60–100 ms once or twice per 30 navigations, Inbox virtualised (≤ 150 rows at 10,000 tasks); full table and verdicts in `docs/HANDOFF.md` §4)_ — `--perf` time-to-first-frame log, idle working-set measurement, Week-view frame budget check, Inbox virtualisation verified, numbers recorded in HANDOFF.
- [x] **P5.2 AOT** _(done 2026-11-19: `dotnet publish src/Doba.App -c Release -r win-x64 -warnaserror` on Windows: 0 trim/AOT warnings, Doba.exe 19.5 MB, `--selfcheck` 9/9 on the published exe; the earlier linux-x64 smoke belongs to the cloud VM and is not repeated on Windows)_ — `dotnet publish -r win-x64` (CI) and `-r linux-x64` (local) with zero trim/AOT warnings.
- [x] **P5.3 CI** _(done 2026-11-19: the pull request `dev → main` (#1) started the one run — build with warnings as errors, 1088 tests, NativeAOT publish and artifact upload all green in 5 m 19 s; on the hosted runner `--perf` printed 377 ms from process start to the first rendered frame and `--selfcheck` passed 9 of 9)_ — GitHub Actions on `windows-latest`: build, test, publish NativeAOT win-x64, upload artifact, print perf; green. Owner's instruction (2026-11-18): no extra Actions runs — everything is verified locally on Windows; the only run is the one the final PR into `main` starts.
- [x] **P5.4 Independent review** _(done 2026-11-19: eight fresh reviewers — Core, Storage, shell/services/platform, screens/controls, dialogs/overlays, motion, KICKOFF conformance, visual incl. 1100×700 — 72 findings (3 blockers, all the missing single-instance guard); fixed by five fixers and a last integration pass: single instance, strict flags, startup and save errors shown, window fitted to the screen, small-window fits, focus after save, projection rules back in Core, transient write retry)_ — fresh reviewer agents: code review of the whole solution and a final visual review of all screenshots; findings fixed.
- [x] **P5.5 Docs** _(done 2026-11-19: README, `docs/HANDOFF.md` with the manual Windows checklist and the final performance table, ARCHITECTURE as built, DECISIONS, design-system, MOTION-REVIEW)_ — README (what, build, run, publish, shortcuts, data location), HANDOFF (verified how, Needs Windows verification with steps, perf numbers, gaps), pull request / branch summary.

## Blocked

(none)

## Needs Windows verification

Verified here by `Doba.exe --selfcheck` (NativeAOT, Windows 11, 9/9): window + icon, Mica actually active, OS animation setting read, hidden message window receives WM_SETTINGCHANGE, Ctrl+Alt+N register/unregister with a simulated press, the managed TrayIcon object created/shown/removed (that Windows shows the icon needs a person), autostart write/read/remove against `HKCU\Software\Doba\SelfCheck`, the real Run value read only.

Still needs a person. Exact steps and expected results: `docs/HANDOFF.md` section 3 (numbers in brackets).

- Tray: close hides, left-click restores, menu Open Doba / New task / Quit, tray setting off → close quits, `--demo` has no tray (1–5).
- Global hotkey Ctrl+Alt+N from another app, with Doba hidden in the tray and minimised; an open dialog is kept (6–9).
- Launch at startup: the real HKCU Run value on the default data location, setting off/on, a start after sign-in hidden in the tray, `--data` / `--demo` leave it alone (10–13).
- Mica look under the chrome tint and the transparency slider; Windows "Transparency effects" toggled while running; acrylic on Windows 10 untested (14–17).
- Title bar on a real window: drag, double-click, snap-layout flyout, ONE click on Maximise / Minimise acts once, caption hover colours, maximised edges, resize limits, mixed-DPI monitors (18–24).
- OS "Animation effects" switched off and on while Doba runs (25).
- Session end: add a task (also right after Enter in the dialog), sign out at once, the task is there after sign-in (26).
- Sleep through midnight: date rolls over, carry-over and backup happen, Calendar opens the new week; time-zone change while open (27–28).
- Completion sound plays a soft click; Open folder opens Explorer; Export / Import native file pickers and their messages under NativeAOT (29–32).
- Second instance on the same data folder brings the window forward (also from the tray); another folder runs separately (33–34).
- Display scaling 125–150 % and a 1366×768 screen: opening size and centring, crisp text and hairlines, every screen usable at 1100×700 (35–38).
- Cold start after reboot: `scripts\perf.ps1 -PrepareCold`, reboot, `scripts\perf.ps1 -Cold`, budget 400 ms (39).
- A mid-range PC or integrated GPU: `scripts\perf.ps1` for the Week frame budget and the blurred dialog fades (40).
