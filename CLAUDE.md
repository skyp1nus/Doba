# Doba — working notes for agents

Doba is a local-only daily planner for Windows: .NET 11 (C# 15), Avalonia 12, CommunityToolkit.Mvvm, SQLite via
Microsoft.Data.Sqlite, NativeAOT; Claude reaches it through `Doba.Mcp.exe` (an MCP stdio server) and `Doba.Bridge`
(a named pipe into the running Doba) while the owner allows it. Read `docs/ARCHITECTURE.md` before changing code; `docs/PROGRESS.md` is the
project state; `docs/DECISIONS.md` lists decisions already made (do not reopen them); `docs/HANDOFF.md` has what was
verified, the manual Windows checks, the measured performance and the known gaps; `docs/design-system.md` is the
token and control reference.

## Design sources (they win over everything else on visuals and motion)

- `docs/design/design-reference.dc.html` — the interactive prototype. Markup (lines 1–822) has exact sizes, colours,
  fonts and copy per screen; the `<script>` (lines 825–1856) has the reference behaviour (seed data, date math,
  `weekData/monthData/yearData/dayData/inboxData/todayData`, dialog logic, settings).
- `docs/design/motion-spec.md` — every animation (Ukrainian; numbers and tokens are unambiguous).
- `docs/design/screens/*.png` — the prototype rendered at 1600×1000 (reference images, when present).
- Our screenshots: `docs/screenshots/*.png`, rendered headlessly with `DemoData` (fixed clock 2026-11-15 22:26).
- Name: the app is **Doba** (the design says "Todo").

## Commands

Development happens on Windows 11 (PowerShell; Git Bash also works — there prefix git commands that take
`rev:path` arguments with `MSYS_NO_PATHCONV=1`). Everything is verified locally; do not trigger GitHub Actions
(rules: `.claude/rules/ci-minutes.md`).

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_NOLOGO='1'; $env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build -warnaserror                       # whole solution; trim + AOT analyzers are on
dotnet test                                     # Microsoft Testing Platform, all test projects
dotnet test --project tests/Doba.Core.Tests     # one project
dotnet test --project tests/Doba.App.Tests -- --filter-method "*Screenshot*"   # e.g. only screenshot tests
dotnet publish src/Doba.App -c Release -r win-x64 -o artifacts\doba-win-x64 -warnaserror   # NativeAOT, 0 warnings (needs the MSVC "Desktop development with C++" tools)
dotnet publish src/Doba.Mcp -c Release -r win-x64 -o artifacts\doba-win-x64 -warnaserror   # Doba.Mcp.exe next to Doba.exe, 0 warnings
$env:DOBA_MCP_EXE="$PWD\artifacts\doba-win-x64\Doba.Mcp.exe"; dotnet test --project tests/Doba.Bridge.Tests   # MCP tests on the published exe
powershell -ExecutionPolicy Bypass -File scripts\perf.ps1    # perf against KICKOFF §7 (publishes, seeds artifacts\perf, measures)
```

Screenshots: tests write PNGs to git-ignored `artifacts/screenshots/`. Only the lead refreshes the tracked
`docs/screenshots/` (`$env:DOBA_SCREENSHOTS='1'` before the test run). Look at your PNGs in `artifacts/screenshots/`
and compare them with `docs/design/screens/<name>.png`.

Each agent works in its own git worktree and builds/tests only there. Use absolute paths under your worktree in
scripts: .NET file APIs called from PowerShell resolve relative paths against the process directory, not the shell's
location. Never run two builds of the same checkout at once — concurrent builds of the same project corrupt each
other's output.

Never touch the real machine from tests or local runs:

- Tests use `PlatformServices.NoOp()`, fakes, or `PlatformServices.Create` with an all-off `PlatformPlan`
  (`new PlatformPlan(false, false, false, false)`: Autostart, Hotkey, Tray, Bridge) — never a plan that allows
  autostart, hotkey or tray. A test that needs the Claude pipe constructs `ClaudeBridge` directly on a data folder
  under `%TEMP%` (`PlatformServices.NoOp() with { Bridge = new ClaudeBridge() }`).
- Launch the app only with `--demo`, `--data <folder under artifacts\>` or `--selfcheck`, and build the argument list
  explicitly. Never without flags: that opens `%LOCALAPPDATA%\Doba` and writes the `HKCU\...\Run` value. A `--data` run
  does register Ctrl+Alt+N and show a tray icon while it runs (never the Run key). The parser is strict (unknown flag
  or a missing `--data` folder → exit 2), but do not rely on that. Leave no Doba process running.
- Run `Doba.Mcp.exe` only with `--data <folder under artifacts\ or %TEMP%>` (it talks to the Doba running on that
  folder; it never starts processes); never without `--data` (that is the owner's real Doba). Leave no `Doba.Mcp`
  process running.
- `Doba.exe` is a GUI exe: capture its output with
  `Start-Process <exe> -ArgumentList … -Wait -PassThru -RedirectStandardOutput <file under artifacts\>`.

## Conventions

- Nullable on, warnings are errors, `latest-recommended` analyzers. Fix warnings; do not suppress them without a
  one-line justification next to the suppression.
- No reflection: compiled bindings (`x:DataType` on every view/template), source-generated JSON and MVVM, no
  `Activator`, no `Type.GetType`. Code must stay NativeAOT/trim clean.
- Time comes from `TimeProvider` (`clock.Today()`, `clock.TimeOfDay()`); never `DateTime.Now`/`DateTime.Today`.
- All "what goes where" logic is in `Doba.Core.Projections` / `Navigation` / `Editing`; views only render records.
- Every colour, radius, font size and spacing comes from `src/Doba.App/Theme/Tokens.axaml`; every duration and
  easing from `Doba.App.Motion.Motion`. `DesignSystem/NoLiteralsTests` enforces it: no colour, duration, easing or
  `Task.Delay(<n>)` literal anywhere in `src/Doba.App` except `Tokens.axaml`, `AccentPalette.cs`, `Motion.cs`,
  `MotionTimes.cs` and `Platform/`; and in `Screens/` and `Controls/` markup no numeric margin, padding, spacing,
  size, radius, font size, line height or stroke other than 0 / ±1 (star weights and `Auto` are fine).
- Animate only `Opacity` and render transforms; never Margin/Width/Height (exception: none — progress bars use ScaleX).
- Platform-specific code goes behind `Doba.App.Platform` interfaces with a no-op fallback.
- Avalonia's API changed between 11 and 12 and memory may be outdated: confirm APIs against the installed package
  (XML docs in `%USERPROFILE%\.nuget\packages\avalonia\12.1.6\lib\net10.0\*.xml`) or the AvaloniaUI/Avalonia GitHub repo before
  relying on them (composition animations, easing, window decorations, transparency, headless capture).
- Tests: xunit v3 (`[Fact]`); UI tests use `[AvaloniaFact]` from Avalonia.Headless.XUnit. Never skip or weaken tests.
- Contracts are lead-only: `Shell/ShellContracts.cs`, `Shell/OverlayViewModel.cs`, `Shell/IAppCommands.cs`,
  `Screens/ScreenViewModel.cs`, shared tokens in `Theme/Tokens.axaml` (a package adds tokens only inside its own
  marked region), `Theme/Theme.axaml`, `Theme/Styles.axaml`, `Theme/Icons.axaml`, the no-literals allowlist, `docs/*`,
  `Directory.*.props`, `.github/**`. Need a change there? Say so in your report.
- An agent commits only on its own worktree branch; the lead architect integrates into `dev`. Never push.
