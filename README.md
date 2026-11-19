# Doba

A minimal, dark, local-only daily planner for Windows — from the year view to today in one click.

Doba keeps an **Inbox** for undated tasks, a **Today** screen with a schedule timeline, and a **Calendar** that zooms
Year → Month → Week → Day. A task has a title, optional notes, an optional date (or a multi-day span of up to 14 days),
an optional time with a duration, a priority (none / low / medium / high / ASAP) and at most one tag.
There are no accounts, no sync, no telemetry and no network calls: everything stays on your PC.

Built with .NET 11, [Avalonia 12](https://avaloniaui.net/) (12.1.6), CommunityToolkit.Mvvm and SQLite
(Microsoft.Data.Sqlite), published as a self-contained NativeAOT executable for Windows x64.

Status, what was verified and how, the measured performance and the known gaps: [`docs/HANDOFF.md`](docs/HANDOFF.md).

## Requirements

- Windows 10 or 11, x64. Development and every measurement so far: Windows 11.
- The .NET 11 SDK. `global.json` asks for 11.0.100 with `rollForward: latestFeature`; `dotnet --list-sdks` should show
  an 11.0.x SDK.
- For the NativeAOT publish only: the MSVC toolchain that NativeAOT links with — Visual Studio or the Visual Studio
  Build Tools with the **Desktop development with C++** workload (the default components include the Windows SDK).
  Build, test and `dotnet run` do not need it.
- Node is **not** needed. It is only used to regenerate the golden test fixtures (`tests/Doba.Core.Tests/Golden/generate.js`).

## Build, test, run

All commands are PowerShell, run from the repository root.

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_NOLOGO='1'; $env:AVALONIA_TELEMETRY_OPTOUT='1'

dotnet build -warnaserror                        # whole solution; warnings are errors, trim + AOT analyzers on
dotnet test                                      # all three test projects (Microsoft Testing Platform, xunit v3)
dotnet test --project tests/Doba.Core.Tests      # one project
dotnet run --project src/Doba.App -- --demo      # the app with the design's demo data; nothing is written to disk
```

`AVALONIA_TELEMETRY_OPTOUT` only affects the build (Avalonia's build tooling); the app itself makes no network calls.

Run `dotnet run --project src/Doba.App` without flags only when you mean it: that opens your real data folder
(`%LOCALAPPDATA%\Doba`), registers the global hotkey and writes the "Launch at startup" Run value.

## Publish (NativeAOT, Windows x64)

```powershell
dotnet publish src/Doba.App -c Release -r win-x64 -o artifacts\doba-win-x64 -warnaserror
.\artifacts\doba-win-x64\Doba.exe --demo
```

The publish reports 0 warnings. ILC also prints two informational "will always throw" lines about Avalonia.Win32
DComposition interop; they are not warnings and come from Avalonia.

The output is `Doba.exe` (about 20 MB) plus the native libraries it loads from the same folder: `e_sqlite3.dll`
(SQLite), `libSkiaSharp.dll` and `libHarfBuzzSharp.dll` (rendering and text shaping) and `av_libglesv2.dll` (ANGLE).
Keep the folder together. Copy it wherever you like; "Launch at startup" stores the path of the exe that wrote it.

CI (`.github/workflows/ci.yml`, one `windows-latest` job) builds in Release with `-warnaserror`, runs the tests,
publishes this build, prints the `--perf` startup line and uploads the folder as the `doba-win-x64` artifact. It runs
only on `workflow_dispatch` or on a pull request into `main` (changes to docs, `*.md` and `tools/` only are skipped).

## Command line

Doba refuses any argument it does not know. An unknown flag, `--data` without a folder (or with an empty one), a flag
where a value should be (`--data --demo`) or an out-of-range number prints the error and a usage line to stderr and
exits with code 2 before anything is opened. Flags are case-insensitive.

| Flag | What it does |
|---|---|
| *(none)* | Normal start on `%LOCALAPPDATA%\Doba`. |
| `--demo` | The design's demo tasks and a fixed clock (Sunday 15 Nov 2026, 22:26). Nothing is read from or written to disk; no autostart, no global hotkey, no tray icon (closing the window quits). |
| `--data <folder>` | Use this data folder instead of `%LOCALAPPDATA%\Doba`. Never touches the Run key. The global hotkey and the tray icon are still used while it runs. |
| `--minimized` | What "Launch at startup" passes: start hidden in the tray (when "Keep running in the tray" is on), otherwise minimised. |
| `--selfcheck` | Opens a demo window, runs nine Windows platform checks, prints one line each to stdout and exits 0 (all passed) or 1. See [HANDOFF](docs/HANDOFF.md#selfcheck). |
| `--perf` | Diagnostics: prints `doba-perf …` lines (time to first frame, startup marks, working set, private bytes) to stdout. |
| `--exit-after-first-frame` | Diagnostics: quit as soon as the first frame with data is on screen (used with `--perf`). |
| `--perf-idle-seconds N` | Diagnostics (implies `--perf`, N = 1–600): stay idle N seconds after the first frame, print memory again, then quit. |
| `--perf-scenario week` | Diagnostics (implies `--perf`): drive the Week view (next / previous, Inbox and back, Month and back), then open and close the New task dialog with and without the backdrop blur; print frame statistics and quit. Writes nothing. |
| `--perf-rounds N` | Diagnostics (N = 1–100): rounds of `--perf-scenario`. |

If several of `--selfcheck`, `--demo` and `--data` are given, `--selfcheck` wins, then `--demo`.

Exit codes: 0 normal (also a second launch that handed over to the running instance), 1 `--selfcheck` with a failed
check, 2 bad command line, 3 the data folder could not be opened or read (a message box names the folder and the
reason; with `--perf` the message goes to stderr only).

`Doba.exe` is a Windows GUI program, so PowerShell does not wait for it or show its output unless you redirect it:

```powershell
$p = Start-Process .\artifacts\doba-win-x64\Doba.exe -ArgumentList '--selfcheck' -Wait -PassThru -RedirectStandardOutput artifacts\selfcheck.txt
Get-Content artifacts\selfcheck.txt; "exit code $($p.ExitCode)"
```

## Keyboard shortcuts

Window-level shortcuts are handled in `src/Doba.App/Shell/KeyboardShortcuts.cs`; Settings › Shortcuts lists the same
keys.

| Keys | Action | Where it works |
|---|---|---|
| `N` | New task | Anywhere the "New task" button is shown (not in Settings), no overlay open, focus not in a text box. |
| `Ctrl` `Alt` `N` | Quick add: shows Doba and opens New task (keeps a task dialog that is already open) | Global, from any app, while Doba runs (also hidden in the tray). Not with `--demo`. |
| `Ctrl` `K` | Search palette | Anywhere, also from a text box; not while an overlay is open. |
| `G` then `I` | Inbox | Within 1.5 s; no overlay open, focus not in a text box. Also from Settings. |
| `G` then `T` | Today | As above. |
| `1` `2` `3` `4` | Year / Month / Week / Day at the current date (top row or numeric keypad) | Also from Inbox, Today and Settings; no overlay, not in a text box. |
| `←` `→` | Previous / next period (repeats while held) | Calendar views only; not while focus is in a text box, list, slider, scrollbar or the time wheel. |
| `Space` | Complete / reopen the focused task (once per press) | A focused list row, week card or day card (any of its buttons); a focused checkbox toggles itself. |
| `Esc` | Close the open popover, then the dialog or search palette | Overlays. In the sidebar's new-tag box it cancels. Otherwise it does nothing (it does not leave Settings). |
| `Enter` | Save the task | Task dialog, focus in the title box (Notes takes Enter as a new line). |
| `↑` `↓` `Enter` | Move through hits, open the selected task | Search palette. |
| `Enter` | Create the tag | Sidebar new-tag box. |
| `↑` `↓` `PgUp` `PgDn` `Home` `End` | Move the wheel | Focused time wheel in the Time popover. |
| `Tab` / `Shift` `Tab` | Move focus, with a visible focus ring | Everywhere. Order: title-bar search, sidebar, page header, content. Tab cycles inside an open overlay. Caption buttons are not tab stops. |

Letters, digits and arrows act only with no modifier held; `Shift`, `Alt`, `Win` or `AltGr` combinations pass through.
On a non-Latin layout (e.g. Ukrainian) the same physical keys work. The "Delete" action in the edit dialog is a button,
not a key.

## Where data lives

Everything is in `%LOCALAPPDATA%\Doba\` (for example `C:\Users\<you>\AppData\Local\Doba\`), or in the `--data` folder:

- `doba.db` — the SQLite database: tasks, tags and settings (WAL mode, so `doba.db-wal` and `doba.db-shm` appear
  beside it while Doba runs). All tasks are loaded into memory at start; changes are written in the background and
  flushed when the window is hidden to the tray, on Quit, on exit and at Windows sign-out / shutdown (at most 3 s there).
- `Backups\doba-yyyy-MM-dd.db` — with Settings › Data › *Daily backup* on (the default), one copy per day, made after the
  first frame and again after midnight; the newest 7 are kept. A leftover `doba-yyyy-MM-dd.db.tmp` from an interrupted
  backup of another day is deleted. Nothing else in `Backups\` is touched. A failed backup is retried the next day.
- `doba.log` — only appears if a write fails: one line per failure (restarted past 256 KB). The title bar then shows
  "Changes not saved" (in the design's danger colour) until restart; its tooltip has the reason and a click opens the
  folder.

Settings › Data:

- **Open folder** opens the data folder in Explorer.
- **Export** saves everything (tasks, tags, settings) as one JSON file, `doba-export-yyyy-MM-dd.json`
  (`{ "format": "doba", "version": 1, … }`).
- **Import** reads such a file and **replaces all tasks, tags and settings at once, without asking**. An invalid file
  changes nothing and the reason is shown under the row.

To start over, quit Doba (tray menu › Quit) and delete or rename the folder. To restore a backup, quit Doba, copy
`Backups\doba-<day>.db` over `doba.db` and delete `doba.db-wal` / `doba.db-shm` if present.

## Running in the background

- **One instance per data folder.** A second launch on the same folder brings the running window forward and exits
  (a second `--minimized` launch just exits). Different `--data` folders run side by side.
- **Tray** (Settings › Windows › *Keep running in the tray*, on by default): closing the window hides it; left-click the
  tray icon to open it; the tray menu has Open Doba / New task / Quit. With the setting off, closing quits.
- **Launch at startup** (on by default): Doba keeps the value `Doba` = `"<path>\Doba.exe" --minimized` under
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` in step with the setting. Only a launch on the default data
  folder touches it (never `--demo`, `--data` or `--selfcheck`).
- **Global hotkey** `Ctrl` `Alt` `N`: registered when the window opens; if another app owns it, Doba carries on without.
- **Backdrop**: Mica on Windows 11, acrylic blur on Windows 10, otherwise a painted backdrop. Settings › Appearance ›
  *Window transparency* sets how much of it shows through the sidebar and title bar.
- **OS "Animation effects" off** makes every animation instant, also when switched while Doba runs (see the
  MotionSync limit in HANDOFF).
- **Carry over unfinished tasks** (Settings › Tasks, on by default): after the first frame and after midnight, open
  single-day tasks dated before today move to today. **Completion sound** is off by default.

## Screenshots and performance

Headless UI tests render the real window with the demo data at 1600×1000. By default the PNGs go to the git-ignored
`artifacts\screenshots\`. To refresh the tracked images in `docs\screenshots\`:

```powershell
$env:DOBA_SCREENSHOTS='1'; dotnet test --project tests/Doba.App.Tests; Remove-Item Env:DOBA_SCREENSHOTS
```

Compare them with the design renders in `docs\design\screens\` (same names).

Performance against the budget in `docs/KICKOFF.md` §7 is measured with the NativeAOT build:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\perf.ps1                 # publish, seed test databases, measure
powershell -ExecutionPolicy Bypass -File scripts\perf.ps1 -SkipPublish -Runs 5 -SkipScenario
powershell -ExecutionPolicy Bypass -File scripts\perf.ps1 -PrepareCold   # then reboot, wait ~2 min, then: -Cold
```

The script writes its test databases under `artifacts\perf\data`, runs Doba only with `--demo` or `--data <folder under
artifacts\>`, prints a table (warm startup, a partly cold start, idle memory, Week and overlay frame times) and saves it
as `artifacts\perf\results-<time>.txt`. Keep the window in front during the scenario; a covered window records no
frames.

## Project layout

```
Doba.sln
src/Doba.Core/          Pure domain: task model, date math, in-memory store, projections, navigation, dialog rules, DemoData
src/Doba.Storage/       SQLite (hand-written SQL, WAL, write-behind writer), daily backup, JSON export / import
src/Doba.App/           Avalonia app (assembly "Doba"): shell, screens, dialogs, controls, theme tokens, motion,
                        Windows platform services, startup, diagnostics
tests/Doba.Core.Tests/     Unit tests, golden tests against the prototype's own script, projection timing
tests/Doba.Storage.Tests/  Database, writer, backup and import / export tests on temp folders
tests/Doba.App.Tests/      Headless UI tests (real Skia rendering), screenshots, keyboard, motion, lifecycle, perf
docs/                   ARCHITECTURE, DECISIONS, PROGRESS, HANDOFF, MOTION-REVIEW, design-system, KICKOFF,
                        design/ (prototype, motion spec, reference renders), screenshots/ (our renders)
scripts/perf.ps1        Performance measurement (see above)
tools/design-render/    Renders the prototype into docs/design/screens with headless Chromium (Node + Playwright)
```

Read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) before changing code. Decisions already made are in
[`docs/DECISIONS.md`](docs/DECISIONS.md); the token and control reference is
[`docs/design-system.md`](docs/design-system.md).

## Licence

Code: Apache License 2.0, see [`LICENSE`](LICENSE).

Fonts: Geist and Geist Mono (Regular, Light, Medium, SemiBold, Bold; Mono Regular and Medium) are embedded in the app
from `src/Doba.App/Assets/Fonts/`. Copyright (c) 2023 Vercel, in collaboration with basement.studio, licensed under the
SIL Open Font License 1.1 ([`OFL.txt`](src/Doba.App/Assets/Fonts/OFL.txt)). The OFL allows bundling them with the app;
the licence file must stay with the fonts, and the fonts may not be sold on their own.
