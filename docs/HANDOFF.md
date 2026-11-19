# Doba — hand-off

For the owner: what was built, what was verified and how, what still needs a person on a real Windows PC, the measured
performance, and what is known to be missing. Describes the final state of `dev` on 2026-11-19 (the commit that adds this text). Build and run
commands are in [`README.md`](../README.md); the code map is [`ARCHITECTURE.md`](ARCHITECTURE.md).

## 1. State

**Branch.** `dev`. The final review fixes and the last integration pass are merged; nothing is merged into `main`.
The single pull request `dev → main` starts the only GitHub Actions run (owner's rule: no other runs); see the pull
request for its result. That run (pull request #1, 2026-11-19) was green: Release build with warnings as errors, 1088 tests, NativeAOT publish, artifact upload; on the hosted runner (no GPU) `--perf` printed 377 ms from process start to the first rendered frame and `--selfcheck` passed 9 of 9.
**What is complete, against the brief (`docs/KICKOFF.md`).**

| Brief | State |
|---|---|
| §6 Shell: custom 40 px title bar, search button, caption buttons, 232 px sidebar with 56 px rail, tags, sliding highlight, page header with switch | Built; screenshots accepted (`week`, `sidebar-collapsed`, `settings-*`). |
| §6 Tasks: checkbox, priority cycle, edit on title click, tag filter dimming | Built and tested. |
| §6 Inbox, Today, Year, Month, Week, Day | Built; each screenshot accepted against `docs/design/screens/`. |
| §6 Task dialog with Date / Time / Priority / Tag popovers and the time wheel | Built; `dialog-date|time|priority|tag.png` accepted. |
| §6 Settings (six tabs) | Built; all six screenshots accepted; every control live. |
| §6 Keyboard and Tab order | Built; every shortcut tested with real headless key events. |
| §6 gaps: search palette, light theme (dark only, "System" = Dark), Sunday week start, window sizes from 1100×700 | Built as logged in `DECISIONS.md`. |
| §7 Performance | Instrumented and measured (section 4); not every line is met. |
| §8 gate 4 (no literal colours / durations) | Enforced by `NoLiteralsTests`, now also easings, delays and layout numbers in screen / control markup. |
| §10 every `PROGRESS.md` item ticked | Yes. |
| §10 accepted screenshot per screen (Inbox, Today, Year, Month, Week, Day, four dialog popovers, six Settings tabs, collapsed sidebar, compact density) | All present in `docs/screenshots/` (41 tracked PNGs, incl. `search.png` and `controls-gallery.png`). |
| §10 motion checklist covered by a test or recorded review | `docs/MOTION-REVIEW.md`: all six checklist items covered by tests; its four findings were fixed afterwards (see there). |
| §10 CI on `windows-latest` builds, tests, publishes NativeAOT, uploads | Workflow in `.github/workflows/ci.yml`; its steps were run locally in the same configuration (Release build, Release tests, AOT publish, `--perf`, `--selfcheck`). It runs once, on the pull request into `main`. |
| §10 README and HANDOFF | This file and `README.md`. |

**Tests.** 1088 tests, all green on the final `dev`, in Debug and in Release (`dotnet test -c Release`, as CI runs
them):

| Project | Tests | Covers |
|---|---|---|
| `Doba.Core.Tests` | 246 | store, date math, projections (incl. the week lane cap, year fit, labels), navigation, dialog rules, carry-over, golden fixtures, projection timing |
| `Doba.Storage.Tests` | 123 | database, write-behind writer incl. failure handling and transient retry, backup, JSON import / export, load timing |
| `Doba.App.Tests` | 719 | headless UI: screens, shell, overlays, keyboard, motion, lifecycle, platform plan, single instance, screenshots, performance |

The counts are from the test runner's own summary of the final run.
**Gates used for every package and fix.** `dotnet build -warnaserror` (0 warnings; trim and AOT analyzers on),
`dotnet test` twice (green), `dotnet publish … -r win-x64 -warnaserror` (0 warnings), screenshots regenerated and
compared (byte-for-byte unchanged unless the change was meant to move pixels), the no-literals test.

Also run on the final `dev`: `dotnet build -c Release -warnaserror` and `dotnet test -c Release --no-build` (what CI runs), `Doba.exe --selfcheck` (9 of 9 checks pass) and `scripts\perf.ps1` (section 4).

## 2. How it was verified

**Unit and golden tests (Core).** The projections, header labels, navigation and dialog rules are compared with the
prototype's own JavaScript: `tests/Doba.Core.Tests/Golden/generate.js` runs `seed()`, `weekData`, `monthData`,
`yearData`, `dayData`, `inboxData`, `todayData`, `renderVals` and the navigation commands under Node and writes JSON
fixtures (348 seed tasks, 44 screen cases, 41 dialog cases, 4 navigation sequences); the C# tests map Core's records
to the same shapes. The fixtures are committed, so the tests need no Node. Unit tests cover what the prototype does
not exercise (ISO weeks and Sunday start, store invariants, carry-over, search above the 50-hit limit, degenerate
measured sizes).

**Storage tests** run on temporary folders: round trips, migrations from empty, flush on dispose, failed bursts
(per-row replay versus whole-burst failure), backup rotation and stale temp files, import validation.

**Headless UI tests** (`Avalonia.Headless.XUnit`, real Skia rendering) open the real `MainWindow` through
`ShellFixture` with the demo data and no-op platform services, press real key events, click, and assert on the view
models and the visual tree: navigation, transitions (handle values, timing, cancellation), overlays and focus, Tab
order, virtualisation, small-window layouts at 1100×700, lifecycle (tray, flush, carry-over, backup) on fake platform
services.

**Screenshot reviews.** Every full-window render (1600×1000, demo data) was compared with the matching reference in
`docs/design/screens/` by a reviewer agent that had not written the code, pixel-measured (track edges, text rows, ink
positions), then fixed and re-checked by a second fresh acceptance pass. The per-package outcome is recorded in
`docs/PROGRESS.md` (P1.4a–P2.9, P4.3). The final visual reviewer compared all 18 reference / render pairs again at full
size; its findings (small-window clipping, tag popover height, ellipses, 1 px label offsets, the selected-day square)
were fixed, except the items in section 5.

**Motion review.** `docs/MOTION-REVIEW.md`: an independent reviewer sampled the real animation handles frame by frame
through the composition renderer and mapped every checklist item and spec row to tests. It reviewed `2a78749`; its four
findings (sidebar highlight timing, view-switch slide on re-entry, strike not re-timed, curves not scanned) were fixed
in the final review pass. Smoothness, feel and the real-display parts are listed in its "Only a person on a real
display can judge" section.

**NativeAOT.** `dotnet publish src/Doba.App -c Release -r win-x64 -o artifacts\doba-win-x64 -warnaserror` gives 0
warnings on every fixer's branch (two informational ILC lines from Avalonia.Win32, not warnings). `Doba.exe` is about
20.1 MB.

<a id="selfcheck"></a>**`Doba.exe --selfcheck`** (last reported: 9/9 on the NativeAOT build, Windows 11, after the
final fixes). It opens a demo window (nothing read from or written to a data folder) and prints one line per check:

| Check | Proves | Does not prove |
|---|---|---|
| `window-icon` | the multi-size app icon loads from the resources | how it looks in the taskbar |
| `window` | the window was created and shown, with a native handle | anything about the title bar's behaviour |
| `backdrop` | Windows reports Mica (Windows 11) or acrylic as active, and the painted fallback is hidden | that it looks right under the tint |
| `os-animations` | the "Animation effects" setting can be read | that a change while running is picked up |
| `platform-window` | the hidden message window exists and a `WM_SETTINGCHANGE` sent to it reaches the code | a real broadcast from Windows |
| `hotkey` | Ctrl+Alt+N registers, a simulated `WM_HOTKEY` raises the event, and it unregisters (if another app owns the key, the line passes and says so) | a real key press from another app |
| `tray` | Avalonia's tray icon object is created, shown, hidden and removed | that Windows actually shows the icon, its menu or clicks |
| `autostart` | writing, reading back and removing the Run-style value works, on `HKCU\Software\Doba\SelfCheck` (deleted afterwards) | the real Run key, or a start after sign-in |
| `run-key` | reads (never writes) whether the real Run value points at this exe; always passes | anything |

**Final independent review.** Eight fresh reviewers read the whole solution against the brief, the prototype and the
docs: Core, Storage, App shell, screens (UI), overlays, motion, conformance to the brief, and a visual pass. They
reported 72 findings (3 blockers — one problem found by three reviewers —, 18 major, 51 minor). Five fixer agents
(Core / Storage, App, UI, overlays, performance) fixed them in separate worktrees; behaviour fixes came with tests that
the fixers checked fail without the fix (by reverting it), plus a few regression-only tests. The lead integrated them. The headline fixes:

- **Single instance per data folder** (the blocker): two processes on one `doba.db` overwrote each other's tasks. Now
  `Program.Main` claims the folder before the database is opened; a second launch brings the running window forward and
  exits.
- **Strict command line**: a mistyped flag no longer falls through to the real data and the Run key (exit 2).
- **Startup errors shown**: a database that cannot be opened (locked, corrupt, from a newer Doba) shows a message box
  naming the folder and exits 3, without creating a fresh database.
- **Failed writes shown and logged**: "Changes not saved" in the title bar and a line in `doba.log`; a burst refused by
  a busy or failing database is no longer replayed op by op with a 30 s wait each.
- **Window fitted to the screen**: opens at 1600×1000 or the screen's working area minus 16 px, never below 1100×700,
  centred.
- **Small windows**: the Day schedule scrolls; Year and Week fit their items and bars to the measured space and count
  the rest in "+N more"; the header subtitle, tag names and the Year stat end in an ellipsis; the Tag popover scrolls
  within the window.
- **Focus after save**: focus returns after the dialog has written, so a deleted or moved card falls back to New task,
  with a visible ring when the opener was reached by keyboard.
- **Data safety in Core / Storage**: store invariants enforced on load, bounded import ids, normalised import tags,
  stale backup temp files removed, bounded search (no full sort per keystroke).
- **Time**: midnight moves the Inbox / Today anchor; a time-zone change re-reads today at once; a dialog open across
  midnight keeps a carried-over date.
- **Motion and gates**: sidebar highlight moves with the content swap; recycled rows show their new state without
  fading; the strike follows OS animations at run time; the no-literals gate covers Theme/, Motion/, easings, delays and
  layout numbers in screen and control markup.
- **Performance**: an 8 MB gen0 GC budget in the AOT build removed the 60–110 ms render stalls; the Week ~40-card load,
  the overlay blur on a real window and warm / partly-cold startup are now measured.

What the review left unfixed, and why, is in section 5.

## 3. Needs Windows verification

Everything below needs a person at the Windows PC. Use the published build:

```powershell
dotnet publish src/Doba.App -c Release -r win-x64 -o artifacts\doba-win-x64 -warnaserror
```

Most steps use a scratch data folder so your real data is not touched, e.g.
`.\artifacts\doba-win-x64\Doba.exe --data $env:TEMP\doba-check` (create nothing first; Doba creates the database).
Steps marked **(real)** need a start without flags, which uses `%LOCALAPPDATA%\Doba` and writes the Run value.
Write the outcome next to each item.

**Tray**

1. Start with `--data $env:TEMP\doba-check`. Close the window with ×. Expected: the window disappears, the Doba icon is
   in the notification area (possibly under the ^ overflow), the process keeps running.
2. Left-click the icon. Expected: the window comes back in the state it had (maximised stays maximised) and is active.
3. Right-click the icon. Expected: a menu Open Doba / New task / Quit. New task shows the window with the New task
   dialog open. Quit ends the process (check Task Manager); no icon is left behind.
4. Settings › Windows › turn off "Keep running in the tray". Expected: the icon disappears; closing the window now ends
   the process.
5. With `--demo`: no tray icon appears and closing the window ends the process.

**Global hotkey**

6. Start with `--data $env:TEMP\doba-check`. Focus another app (e.g. Notepad) and press Ctrl+Alt+N. Expected: Doba comes
   to the front with the New task dialog open and the title box focused.
7. Repeat with Doba hidden in the tray, and with Doba minimised. Expected: the same.
8. Type a title in the dialog, switch to Notepad, press Ctrl+Alt+N. Expected: the same dialog, text kept (not replaced).
9. With `--demo`, Ctrl+Alt+N does nothing (not registered). If step 6 does nothing at all, check whether another app
   owns Ctrl+Alt+N (`--selfcheck` says "in use by another app").

**Launch at startup** (real)

10. Copy `artifacts\doba-win-x64` to a permanent folder, e.g. `C:\Apps\Doba`, and start `C:\Apps\Doba\Doba.exe` once
    without flags. Then run
    `Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run | Select-Object Doba`.
    Expected: `"C:\Apps\Doba\Doba.exe" --minimized` (written after the first frame).
11. Settings › Windows › turn off "Launch at startup", run the command again. Expected: no `Doba` value. Turn it back on.
    Expected: the value is back.
12. Sign out and in (or restart). Expected: Doba starts by itself with no window, only the tray icon; left-click shows
    it; Ctrl+Alt+N works.
13. Start a `--data` or `--demo` launch. Expected: the Run value is unchanged.

**Backdrop and transparency**

14. Windows 11, on the default dark wallpaper and on a bright one. Expected: the sidebar and title bar show the
    desktop through Mica under the dark tint; the main panel stays solid; the three painted glows are not drawn on top.
15. Settings › Appearance › Window transparency: drag from one end to the other. Expected: the tint over sidebar and
    title bar changes smoothly (300 ms cross-fade) and the text stays readable at both ends.
16. Windows Settings › Personalization › Colors › Transparency effects off, then on, while Doba runs. Record what
    happens (not tested by anyone; Doba does not listen for this setting).
17. Acrylic on Windows 10: untested (no Windows 10 PC). If you have one, repeat 14–15 there.

**Title bar on a real window**

18. Drag the window by an empty part of the title bar. Expected: it moves. Double-click it. Expected: maximise, and
    double-click again restores.
19. Hover the Maximise button. Expected: the Windows 11 snap-layout flyout appears and works.
20. Click Maximise **once**. Expected: the window maximises and stays maximised (not maximise-and-restore in one click).
    Click Minimise once. Expected: it minimises once; restore it from the taskbar. (The buttons carry both Avalonia's
    window-decoration role and a click handler; whether that double-fires is unknown.)
21. Hover each caption button. Expected: Minimise and Maximise get a faint light background; Close turns red (#C42B1C).
22. Maximised: the title bar is fully visible at the top and nothing is cut off at the screen edges.
23. Resize from every edge and corner. Expected: it stops at 1100×700.
24. With two monitors at different scaling, drag the window across. Expected: crisp text and the right size on each.

**OS animations off**

25. Follow `docs/MOTION-REVIEW.md` "Only a person on a real display can judge", item 9: turn off Settings ›
    Accessibility › Visual effects › Animation effects while Doba runs. Expected: zoom, slides, dialog, toggles and the
    task strike all change at once. Turn it back on. Expected: the transitions return. Known limit: if Doba was
    started with animations off, hover and colour fades of controls that already exist stay instant until restart.

**Session end and midnight**

26. With `--data $env:TEMP\doba-check` (or real): add a task, then sign out at once without closing Doba. Sign in, start
    Doba on the same folder. Expected: the task is there. Repeat, pressing Enter in the dialog and signing out within a
    second.
27. Leave Doba open (window or tray) before midnight with "Carry over unfinished tasks" and "Daily backup" on, and an
    open task dated today. Put the PC to sleep; wake it after midnight and open Doba. Expected: Today shows the new date;
    the task from yesterday is now on today; `Backups\doba-<new date>.db` exists; the sidebar's Calendar opens the new
    day's week.
28. Change the time zone in Windows Settings so the date changes while Doba is open. Expected: Today, the header and
    the now-line follow at once.

**Sound, folder, files**

29. Settings › Tasks › turn on "Completion sound"; tick a task. Expected: one short, soft click (40 ms, synthesized).
    Unticking makes no sound.
30. Settings › Data › Open folder. Expected: Explorer opens the folder shown in the row. (In `--demo` it shows
    `%LOCALAPPDATA%\Doba` and does nothing if that folder does not exist.)
31. Settings › Data › Export. Expected: a Save dialog proposing `doba-export-<date>.json`; after saving, "Exported N
    tasks." under the row. Cancel does nothing.
32. Import that file into another scratch folder. Expected: an Open dialog; "Imported N tasks."; all tasks, tags and
    settings replaced, without a confirmation. Import a file that is not a Doba export (e.g. any `.txt` renamed to
    `.json`). Expected: an error under the row, nothing changed. Also try a file you cannot read (e.g. in use by another
    program). Expected: "Import failed: the file could not be read." (or "…access to the file was denied.").

**Second instance**

33. With Doba running on `--data $env:TEMP\doba-check`, start the same command again. Expected: the running window comes
    to the front; the second process exits at once (exit code 0); no second tray icon. Repeat with Doba hidden in the
    tray. Expected: the window is shown.
34. Start a second Doba on another `--data` folder. Expected: a second, independent window.

**Display scaling and small screens**

35. 1920×1080 at 125 %: start Doba. Expected: the window opens centred at about 1504×793, caption buttons and the bottom
    edge on screen. Same at 100 % on a 1366×768 screen: about 1334×700.
36. 1920×1080 at 150 % (work area about 1280×688): by the code the window opens at 1248×700, slightly taller than the
    work area, so its bottom edge may sit under the taskbar. Record whether that is acceptable.
37. At 125 % and 150 %, look at text and 1 px lines (checkbox rings, rules, strikes): crisp, not blurred.
38. Resize to 1100×700 and visit Today, Week, Month, Year, Day and the task dialog with each popover. Expected: nothing
    cut off without a "+N more", scrollbar or ellipsis. Known: Overdue rows on Today crowd their titles.

**Performance on other hardware**

39. Cold start: `powershell -ExecutionPolicy Bypass -File scripts\perf.ps1 -PrepareCold`, reboot, sign in, wait about
    two minutes, start nothing else, then `powershell -ExecutionPolicy Bypass -File scripts\perf.ps1 -Cold`. Budget:
    first frame within 400 ms. Put the "demo COLD" and "db-10k database COLD" lines into section 4.
40. On a mid-range PC or one with an integrated GPU: run `scripts\perf.ps1` (window in front, unobstructed). Compare
    the Week scenario (render p95, frames over 16.7 / 33 ms) and the overlay table (blur versus plain). If the blurred
    fades show frames well over 20 ms there, the blur can be dropped (`DECISIONS.md` "Backdrop blur behind overlays").
    Also open and close the task dialog by hand (N, Esc) and judge whether it stutters.

**Schedule: drag, resize, keyboard, "+N"** (Today and Day; headless tests cannot judge how a drag feels)

Start with `--demo` (its 15 and 16 November have overlapping tasks) unless the step says otherwise. Steps 41–56 are
the same on Today and in the Day view; try each on both at least once.

41. Hover a schedule block. Expected: a faint lighter layer and a short bar at the bottom edge fade in (about 0.2 s);
    the cursor is a hand; over the bar it is a vertical resize arrow.
42. Click a block without moving the mouse. Expected: its task opens in the dialog; nothing is marked done. Click the
    small checkbox in a block. Expected: the task is marked done (or not done) and no dialog opens.
43. Press on "Write the weekly update", let the mouse tremble a little (under 4 px) and release. Expected: the dialog
    opens; the time is unchanged.
44. Drag that block down. Expected: after about 4 px the copy lifts (a touch larger, with a shadow); the old place
    shows a dashed outline; a time label in the hour column shows the slot; the copy steps in 15-minute slots;
    blocks under the copy fade to about half. Release: the block lands in about 0.2 s, the list on the left shows the
    new time, and the neighbours slide to their new columns. No dialog opens.
45. Drag again holding Alt; also press and release Alt without moving. Expected: 5-minute slots, re-snapped at once
    on the Alt press or release; no window menu opens and no access-key underlines appear.
46. Drag far up and far down. Expected: the block stops at 07:00, and with its end at 23:00, without a bounce. Then
    make the window 1100×700 and drag towards the top and the bottom of the schedule. Expected: it scrolls by itself,
    faster closer to the edge, and stops at the ends.
47. Press Esc during a drag, and during a resize. Expected: the copy flies back (about 0.2 s), the release does not
    open the dialog, nothing is saved.
48. Alt+Tab to another app during a drag (button still down), then come back. Expected: the drag was cancelled as by
    Esc: nothing moved and the cursor is normal.
49. Drag a block's bottom bar. Expected: the block grows or shrinks in place in 15-minute steps (5 with Alt), shows
    the range and length ("09:30 – 11:15 · 1h 45m"); at least 15 minutes; its end at most 23:00; release saves.
50. Keyboard: Tab to a block (an outline appears around it). Up / Down move it by 15 minutes (Alt: 5); Shift+Up /
    Down change its length; the time label stays about 1 s after the last step; holding a key repeats; at 07:00 /
    23:00 nothing happens and the column does not scroll. Enter opens the task; Space ticks it. In the Day view
    Left / Right still change the day.
51. Narrator (Win+Ctrl+Enter). Expected after a drop: "Moved <title>, 11:15 to 12:15"; after a resize: "Resized …";
    after Esc: "Move cancelled"; after a key step: the new time. Repeat the same step twice: read both times.
52. Day view, 16 November: two blocks and "+2" beside them. Click "+2". Expected: a list of two tasks opens under it
    (about 0.2 s; near the bottom of the day it opens above). A second click, Esc, a click elsewhere in the schedule
    or another day closes it. A row's title opens the task (the list closes); its checkbox ticks it (the list stays).
    Tasks in the list cannot be dragged. Keyboard: Tab to "+2", Enter: focus on the first row; Esc: focus back on
    "+2". Moving a block with the keyboard under a "+N" moves the focus to that "+N".
53. Windows Settings › Accessibility › Visual effects › Animation effects off, while Doba runs. Drag a block.
    Expected: the copy follows the pointer without the lift and the shadow; the drop and Esc are instant; neighbours
    jump. The time label after a key step still stays about 1 s. Turn the setting back on.
54. At 125 % and 150 % display scaling: a slight tremble on click still opens rather than drags; the copy, the dashed
    outline and the bar are crisp; the bottom bar is easy to grab (it reaches 3 px below the block).
55. If a pen is available: the pen drags like the mouse. On a touch screen: a tap opens or ticks; touch does not drag
    (by decision).
56. With `--data D:\dev\Doba\artifacts\manual` (or a scratch folder): move a block, close Doba (Quit from the tray),
    start again. Expected: the new time is kept. A task with no length set (drawn as 30 minutes) still has no length
    after a move; a resize sets one.

## 4. Performance

Final run of `scripts\perf.ps1` on `dev` at `938bdb9` (2026-11-19, quiet machine, nothing else building).
Machine for every number: AMD Ryzen 7 5800X3D (96 MB L3), 32 GB, Radeon RX 9070 XT, 180 Hz display, Windows 11 build
26300. This is a high-end desktop; no mid-range PC and no integrated GPU was measured. NativeAOT `win-x64` build.

| Budget (KICKOFF §7) | Measured | Verdict |
|---|---|---|
| First frame with real data within 400 ms **cold**, AOT, mid-range PC | **Warm** (exe, DLLs and database in the OS file cache), from process start to the first frame with data drawn, median / worst of 10: demo 216 / 246 ms, 348 tasks 216 / 246 ms, 10,000 tasks 233 / 251 ms, fresh empty folder 215 / 243 ms. First launch right after publish (also warm): 330 ms. **Partly cold** (`--demo` from a copy of the build written with unbuffered I/O, so `Doba.exe` and `e_sqlite3.dll` come from disk and the antivirus scans the new exe; Windows' own DLLs warm): 1002, 986 and 952 ms; the same copy again 198–238 ms. **True cold** (after a reboot): not measured. | Warm: met on this PC. Cold: **not measured**; the partly-cold proxy is far over 400 ms, mostly the first read and antivirus scan of a new exe. Mid-range PC: not measured. |
| Idle working set under 120 MB (aim) | After 5 s on the launch screen, working set / private bytes: demo 106.7 / 124.6 MB, 348 tasks 109.9 / 126.3 MB, 10,000 tasks 122.2 / 138.5 MB (managed heap 10–19 MB; the rest is the renderer, fonts and the GPU cache). | Met for demo and 348 tasks; **2 MB over at 10,000 tasks**. About 15 MB of the idle set is render and composition memory that the launch animation allocates early (the first content transition would allocate it anyway; nothing leaks). |
| Each projection under 5 ms at 10,000 tasks | Average per call: Inbox 0.87, Today 0.72, Day 0.006, Week 0.02, Month 0.045, Year 0.10, Sidebar 0.16, carry-over selection 0.07 ms (Release, the Core reviewer's run); one-character Search about 1 ms after the bounded-search fix. The test asserts each average under 20 ms (the budget × 4 for CI) and the one-character Search median under 5 ms. | Met. |
| 60 fps on the Week view with about 40 cards; animate only opacity and transforms on the render thread | Load: `db-dense-compact` (10,000 tasks plus a filled current week, compact density) = 38 cards + 5 bars at 1600×1000; comfortable density holds at most 29 + 5. Week scenario, 5 rounds × 6 navigations, two runs each, render thread p50 / p95 / max: dense 5.6 / 5.6 / 27.8 ms; 10,000 tasks (29 + 5) 5.6 / 5.6 / 26.2 and 22.1 ms; demo (24 + 3) 5.6 / 5.6 / 17.7 and 17.2 ms. No render frame over 33 ms in any run; 3–7 frames over 16.7 ms per 30 navigations. **UI thread**: max 64–99 ms, 2 frames over 50 ms per run. | Render thread (where the animations run): met apart from a few 17–28 ms frames. UI thread: **not fully met** — one or two pauses of 60–100 ms per 30 navigations, at a content swap, between the old screen's exit and the new one's entrance (not a stutter inside a running animation). Moving between weeks now reuses the cards (allocation per step fell from 13–16 MB to about 5 MB), which removed most long frames but not the worst one. |
| Overlay (task dialog) fades, with the 3 px backdrop blur | New task dialog opened and closed 8 times with blur and 8 without, per data set, render thread inside the fades: with blur open p95 5.6–5.7 ms, max 11–17 ms; close p95 5.7–10.6 ms, max 11–22 ms (0–2 frames over 16.7 ms per 8 fades); without blur max 7–17 ms. | Met on this GPU (no frame over 33 ms), so the blur stays. Integrated GPU: not measured. |
| Virtualise anything that can grow without bound (Inbox) | Inbox and Today lists are virtualised. Headless test with 10,000 undated tasks: at most 150 rows realised at the top and at the end of the list. | Met (headless; not timed on a real window). |
| No reflection on hot paths or at startup | Compiled bindings, source-generated JSON and MVVM; NativeAOT publish with 0 trim / AOT warnings. | Met. |

GC note: the AOT build embeds an 8 MB gen0 budget (`Doba.App.csproj`, `IlcArg --runtimeopt:GCgen0size=800000`). With
the default (about 30 MB on this CPU) the render thread stalled 60–110 ms during Week navigation; with 8 MB the worst
render frame is about 28 ms. `DOTNET_GCgen0size` overrides it at run time. The Skia GPU resource cache is capped at
24 MB (Avalonia's default is about 28 MB); it saves about 3 MB idle and was the largest cap that left frame times
unchanged here — on a much larger or high-DPI window it is worth re-checking with `scripts\perf.ps1`. The candidate
tables are in `DECISIONS.md`.
## 5. Known gaps and accepted deviations

Accepted deviations (each has a line in `DECISIONS.md`):

- **Whole-pixel layout**: Avalonia rounds each measured size up where Chromium keeps fractions, so some stacks land
  1 px off the reference, and segmented controls / text buttons with several labels are 1–2 px wider.
- **Text gamma**: glyph boxes match the references, but stems carry a little more ink (Skia's text gamma, not
  configurable in Avalonia 12.1.6). The header ‹ › chevrons draw about 15 % lighter than the reference (rasteriser
  difference; the stroke values equal the design's).
- **Light theme** is not designed: only the dark palette exists; "System" renders as Dark. The token structure has a
  place for a second palette.
- **Compact Week** shows as many cards as actually fit, not the prototype's fixed compact heuristic.
- **Zoom blur** (6 px during zoom transitions) is not drawn (optional in the spec); **hover brightness** on bars and
  schedule blocks is a 4 % white overlay instead of `brightness(1.2)`.
- **MotionSync limit**: a XAML transition built while OS animations were off stays instant until its control is
  re-templated or Doba restarts (it only errs towards no animation).
- **Sidebar collapse** changes the column widths once per toggle; the content reflows once (start of a collapse, end of
  an expand) instead of continuously.
- **Month** draws at most two span lanes per week; a span that would need a third lane is counted in "+N more" of each
  day it covers (the prototype drops it silently).
- **Window placement** (size, position, maximised) is not remembered between launches; the window opens fitted to the
  screen each time.

Known gaps left by the final review (not fixed, with the reason):

- **Overdue rows at 1100 px** on Today crowd the title to a few characters when a tag, a date and "→ Today" are all
  shown. Needs a design decision (hide the tag, as Week cards do) and a measure pass in a virtualised row.
- **Caption buttons are wired twice** (Avalonia's window-decoration role and a click handler). Whether one click acts
  twice on a real window is unverified (section 3, step 20).
- **A few display strings are still built in the App**: task-group headers are upper-cased by a converter and the
  schedule's fixed "07–23" range label lives in the timeline control. Everything else the brief lists (grouping,
  sorting, lanes, overflow counts, fitting, labels) comes from Core.
- **Week next / previous**: one or two UI-thread pauses of 60–100 ms per 30 navigations remain (section 4).
- **Idle memory at 10,000 tasks** is 122 MB, 2 MB over the 120 MB aim (section 4).
- **New-row entrance on a reused week card**: a card that is re-pointed when the week changes does not play the
  entrance; a just-created task is not expected in that situation.
- **Tray icon** (found by the owner on 2026-11-19, fixed): the icon never appeared, because an Avalonia 12 `TrayIcon` gets
  its native icon only once it is in the application's `TrayIcon.Icons` collection and Doba created it standalone;
  closing then hid the window with nothing to bring it back. The icon is now attached there, `IsSupported` turns false
  when no native icon could be created (closing then quits), and `--selfcheck` fails without the native icon.
- **Daily backup failures** go to the debugger trace only (not to `doba.log`, no notice).
- **Database rows are not validated on load**; the store normalises every task it loads instead (bad values are
  corrected in memory and on the next write of that task).
- **No unhandled-exception handler** flushes pending writes before a crash.
- **Small offsets**: dialog pills and the popovers that follow them are 1–2 px right of the reference (width
  rounding); settings rows step 71 px where the reference's first row is 70 px, so segment groups from the second row
  on sit up to 1 px low.
- **CI** runs once, on the pull request. Its `--perf` and `--selfcheck` steps are informational on a hosted runner
  (no GPU, no Mica, possibly no notification area): they print their output but cannot fail the job.

Schedule drag (2026-11-19), known and left as they are:

- **"+N" open state** is not exposed to screen readers as expanded / collapsed (no `aria-expanded` equivalent on an
  Avalonia Button); its name says how many tasks and which times it holds.
- **While a block lands** (0.2 s) the whole blocks layer is drawn over the now-line, not only the landing block as in
  the prototype: another block crossing the line hides it for that moment.
- **Shadows and dashes**: the lifted copy's and the "+N" list's box shadows spread 1–2 colour levels differently from
  Chromium, and the dash pattern on vertical edges starts at another phase.
- **Touch**: no long-press drag (the prototype has none either).

Repository housekeeping for the owner:

- Two untracked files at the root of your checkout are yours, not part of the build: `KICKOFF.md` (a duplicate of
  `docs/KICKOFF.md`) and `scripts/cloud-setup.sh` (the setup script of the original Ubuntu cloud session). Keep or
  delete them.
- `.claude/settings.json` installs a Stop hook (`.claude/hooks/keep-going.sh`) for Claude Code sessions in this repo:
  while `docs/PROGRESS.md` has an unticked `- [ ]` item (and no `docs/BLOCKED.md` exists) it blocks the session from
  stopping, up to five attempts with an unchanged PROGRESS.md. It goes quiet once every item is ticked; remove the
  hook if you do not want it in your own sessions.

## 6. Incidents during the build

Reported by the lead; the current machine state was re-checked for this document.

- **Edits in the main checkout.** Two agents briefly modified files in `D:\dev\Doba` (the main checkout) instead of
  their own worktrees, because .NET file APIs called from PowerShell resolved relative paths against the process
  directory. Both reverted their changes. Since then every agent brief requires absolute paths under its worktree.
- **An unflagged launch.** One agent's performance script once started `Doba.exe` with no flags. That opened the real
  data location: it created `%LOCALAPPDATA%\Doba` (an empty database and one backup) and wrote the HKCU Run value
  `Doba`. Both were removed and verified absent. On 2026-11-19, for this document: `%LOCALAPPDATA%\Doba` does not
  exist, the Run key has no `Doba` value, `HKCU\Software\Doba` does not exist, and no Doba process is running.
  This cannot recur silently: the command line is now strict (an unknown or malformed flag exits 2 before anything is
  opened), and `scripts\perf.ps1` refuses to start Doba without `--demo` or a `--data` folder under `artifacts\`.
