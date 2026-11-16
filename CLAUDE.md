# Doba — working notes for agents

Doba is a local-only daily planner for Windows: .NET 11 (C# 15), Avalonia 12, CommunityToolkit.Mvvm, SQLite via
Microsoft.Data.Sqlite, NativeAOT. Read `docs/ARCHITECTURE.md` before changing code; `docs/PROGRESS.md` is the
project state; `docs/DECISIONS.md` lists decisions already made (do not reopen them).

## Design sources (they win over everything else on visuals and motion)

- `docs/design/design-reference.dc.html` — the interactive prototype. Markup (lines 1–822) has exact sizes, colours,
  fonts and copy per screen; the `<script>` (lines 825–1856) has the reference behaviour (seed data, date math,
  `weekData/monthData/yearData/dayData/inboxData/todayData`, dialog logic, settings).
- `docs/design/motion-spec.md` — every animation (Ukrainian; numbers and tokens are unambiguous).
- `docs/design/screens/*.png` — the prototype rendered at 1600×1000 (reference images, when present).
- Our screenshots: `docs/screenshots/*.png`, rendered headlessly with `DemoData` (fixed clock 2026-11-15 22:26).
- Name: the app is **Doba** (the design says "Todo").

## Commands

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 AVALONIA_TELEMETRY_OPTOUT=1
dotnet build -warnaserror                       # whole solution; trim + AOT analyzers are on
dotnet test                                     # Microsoft Testing Platform, all test projects
dotnet test --project tests/Doba.Core.Tests     # one project
dotnet test --project tests/Doba.App.Tests -- --filter-method "*Screenshot*"   # e.g. only screenshot tests
dotnet publish src/Doba.App -r linux-x64 -c Release -o /tmp/doba-aot   # local NativeAOT smoke (CI does win-x64)
```

When several agents work in parallel without worktrees, build and test **only your own projects**
(`dotnet build src/Doba.Core`, `dotnet test --project tests/Doba.Core.Tests`) — concurrent builds of the same project
corrupt each other's output.

## Conventions

- Nullable on, warnings are errors, `latest-recommended` analyzers. Fix warnings; do not suppress them without a
  one-line justification next to the suppression.
- No reflection: compiled bindings (`x:DataType` on every view/template), source-generated JSON and MVVM, no
  `Activator`, no `Type.GetType`. Code must stay NativeAOT/trim clean.
- Time comes from `TimeProvider` (`clock.Today()`, `clock.TimeOfDay()`); never `DateTime.Now`/`DateTime.Today`.
- All "what goes where" logic is in `Doba.Core.Projections` / `Navigation` / `Editing`; views only render records.
- Every colour, radius, font size and spacing comes from `src/Doba.App/Theme/Tokens.axaml`; every duration and
  easing from `Doba.App.Motion.Motion`. No `#RRGGBB` or millisecond literals in views (a test enforces this).
- Animate only `Opacity` and render transforms; never Margin/Width/Height (exception: none — progress bars use ScaleX).
- Platform-specific code goes behind `Doba.App.Platform` interfaces with a no-op fallback.
- Avalonia's API changed between 11 and 12 and memory may be outdated: confirm APIs against the installed package
  (XML docs in `~/.nuget/packages/avalonia/12.1.6/lib/net10.0/*.xml`) or the AvaloniaUI/Avalonia GitHub repo before
  relying on them (composition animations, easing, window decorations, transparency, headless capture).
- Tests: xunit v3 (`[Fact]`); UI tests use `[AvaloniaFact]` from Avalonia.Headless.XUnit. Never skip or weaken tests.
- Commits are made by the lead architect; agents report what they changed.
