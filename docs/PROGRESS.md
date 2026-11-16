# Progress

The state of the project. Tick an item only when its gates pass: `dotnet build -warnaserror` clean (incl. trim/AOT
analyzers), `dotnet test` green, and for UI work screenshots regenerated at 1600×1000 with DemoData and accepted by a
fresh reviewer agent, with no literal colours/durations outside tokens and `Motion`.

## Phase 0 — Bootstrap

- [x] **P0 Bootstrap** — SDK, design read, ARCHITECTURE/PROGRESS/DECISIONS/CLAUDE.md, solution scaffold with analyzers + central versions + CI, headless screenshot test, Core/Storage/App contracts, DemoData contract.
  - Also done by the lead: `KeyedSync` (row reconciliation), `DayClock` (minute tick + midnight), `--perf/--exit-after-first-frame/--demo/--data` flags.
  - Notes: SDK 11.0.100 from packages.microsoft.com (Debian 12 feed). AOT publish (linux-x64) of the scaffold: 0 warnings.

## Phase 1 — Foundations

- [ ] **P1.1 Core** _(in progress, worktree agent)_ — model, `Tags.Normalize`, `DateMath`, `TaskStore` (indexes, events, persistence calls), `Projector` (Inbox, Today, Day, Week, Month, Year, Sidebar, Search), `ScheduleLayout`, `Navigator` + `HeaderProjection`, `TaskDialog` rules, `DemoData` (exact seed incl. mulberry32 fill).
  - Accept: golden tests vs the prototype JS run under Node for all six screen projections + header + dialog labels; unit tests for date math (ISO weeks, Sunday start), store events, navigation; perf test: each projection < 5 ms avg on 10,000 tasks (CI margin ×4).
- [x] **P1.2 Storage** — `DobaDatabase` (schema v1, WAL, user_version migrations, write-behind channel, flush on dispose), settings persistence, `Backup.RunDailyAsync` (keep 7), `JsonTransfer` (source-generated).
  - Notes: 88 tests. Write-behind channel, burst transactions (≤4096 ops), per-op replay on failure, errors surface from FlushAsync. Load 10k ≈ 15–22 ms; persist 10k ≈ 75–112 ms (AOT 47–80 ms). AOT smoke publish of a Storage console app: 0 warnings. Ships e_sqlite3 native lib beside the exe.
  - Accept: round-trip tests (tasks/tags/settings), crash-safety (dispose flushes), migration from empty, backup rotation, JSON export→import equality, invalid JSON → FormatException; AOT/trim analyzers clean.
- [ ] **P1.3 Design system** _(in progress, worktree agent)_ — `Theme/Tokens.axaml`, fonts, ControlThemes (buttons: primary/outline/ghost/icon, checkbox, priority icon, segmented switch with sliding indicator, toggle switch, pill, kbd, text box, scrollbars, menu item), accent as dynamic resources, density resources, `Motion` animator helpers, literal-colour/duration grep test, a controls gallery screenshot.
  - Accept: gallery screenshot reviewed against the prototype's control styles; grep test passes.
- [ ] **P1.4 Shell** — `AppServices` composition root, `--demo/--data/--perf` flags, `MainWindow` custom chrome (40 px title bar, search button, caption buttons with element roles), sidebar (Inbox/Today/Calendar with counts, Tags with inline new-tag input + filter, Collapse → 56 px rail, Settings, sliding highlight), page header (crumbs, title+subtitle, Year/Month/Week/Day switch with sliding indicator, Today, ‹ ›, New task), `MainViewModel` with `NavState`, `KeyedSync`, `DayClock`, screen host with placeholder screens, screenshot harness (`ShellFixture`) for every view.
  - Accept: shell screenshot (week placeholder) + collapsed-sidebar screenshot reviewed; navigation unit tests through the VM; New task/keyboard hooks stubbed for Phase 2.

## Phase 2 — Screens

- [ ] **P2.1 Inbox** — two columns, groups, rows (checkbox, priority, title, tag, meta), Add task row, virtualised list. Screenshot `inbox.png`.
- [ ] **P2.2 Today** — summary + progress, groups incl. Overdue with "→ Today", In progress, Scheduled, Anytime, Done; schedule timeline with now-line. Screenshot `today.png`.
- [ ] **P2.3 Week** — two rows of day cells, lanes of multi-day bars, cards, "+N more" from measured size, next-week preview, per-day progress. Screenshot `week.png`.
- [ ] **P2.4 Month** — weekday header, week rows with W-numbers, day cells, span bars (2 lanes), "+N more". Screenshot `month.png`.
- [ ] **P2.5 Year** — 4×3 months with stats, progress, key items, "+N more". Screenshot `year.png`.
- [ ] **P2.6 Day** — spans, task cards with meta, Add task, timeline. Screenshot `day.png`.
- [ ] **P2.7 Task dialog** — title, notes, Date/Time/Priority/Tag pills and popovers (mini calendar, quick options, lasts N days; time wheel, duration; priority list; tag list), Enter/Esc/Delete rules, Create disabled until a title. Screenshots `dialog-date.png`, `dialog-time.png`, `dialog-priority.png`, `dialog-tag.png`.
- [ ] **P2.8 Settings** — settings sidebar, six tabs with live controls bound to `ISettingsService`. Screenshots `settings-*.png` (6).
- [ ] **P2.9 Density + rail** — compact density across screens; collapsed sidebar rail. Screenshots `density-compact.png`, `sidebar-collapsed.png`.

## Phase 3 — Motion

- [ ] **P3.1 Transitions** — zoom in/out from click point, period slides, section exit+enter with direction, settings in/out (+ sidebar slide), title fade, launch, sidebar collapse; cancellation on rapid clicks.
- [ ] **P3.2 Micro-interactions and overlays** — checkbox/strike/dim, new-row entrance, tag-filter dim, progress scale, toggles, segments, hovers, dialog open/close (focus return), popovers, wheel text, OS animations-off.
- [ ] **P3.3 Motion acceptance** — every item of the motion-spec checklist covered by a test or recorded review (`docs/MOTION-REVIEW.md`).

## Phase 4 — Platform and remaining features

- [ ] **P4.1 Windows platform** _(services done: autostart, hotkey, preferences, shell/sound — wiring of tray/backdrop/hotkey into the shell pending)_ — tray (keep running in tray), autostart (HKCU Run), global hotkey Ctrl+Alt+N, backdrop transparency (Mica/acrylic + tint from the transparency setting), OS animation setting, open folder, completion sound; no-op fallbacks.
- [ ] **P4.2 Data features** — daily backup wiring, Export / Import (file pickers), Open folder, settings persistence wiring, carry-over at startup and midnight, midnight rollover.
- [ ] **P4.3 Search palette** — Ctrl K overlay in the dialog style, substring search, arrows + Enter opens the task. Screenshot `search.png`.
- [ ] **P4.4 Keyboard** — N, Ctrl K, G I, G T, 1–4, ← →, Space on focused task, Esc; Tab order with visible focus everywhere.

## Phase 5 — Hardening

- [ ] **P5.1 Performance** — `--perf` time-to-first-frame log, idle working-set measurement, Week-view frame budget check, Inbox virtualisation verified, numbers recorded in HANDOFF.
- [ ] **P5.2 AOT** — `dotnet publish -r win-x64` (CI) and `-r linux-x64` (local) with zero trim/AOT warnings.
- [ ] **P5.3 CI** — GitHub Actions on `windows-latest`: build, test, publish NativeAOT win-x64, upload artifact, print perf; green.
- [ ] **P5.4 Independent review** — fresh reviewer agents: code review of the whole solution and a final visual review of all screenshots; findings fixed.
- [ ] **P5.5 Docs** — README (what, build, run, publish, shortcuts, data location), HANDOFF (verified how, Needs Windows verification with steps, perf numbers, gaps), pull request / branch summary.

## Blocked

(none)

## Needs Windows verification

(filled in during Phase 4/5; copied to HANDOFF.md)
