# Doba

A minimal, dark, local-only daily planner for Windows — from the year view to today in one click.

Doba keeps an **Inbox** for undated tasks, a **Today** screen with a schedule timeline, and a **Calendar** that zooms
Year → Month → Week → Day. A task has a title, optional notes, an optional date (or a multi-day span of up to 14 days),
an optional time with a duration, a priority (none / low / medium / high / ASAP) and at most one tag.
There are no accounts, no sync, no telemetry and no network calls: everything stays on your PC.

Built with .NET 11, [Avalonia 12](https://avaloniaui.net/), CommunityToolkit.Mvvm and SQLite, published as a
self-contained NativeAOT executable.

## Build and run

Requirements: the .NET 11 SDK (`dotnet --list-sdks` should show 11.0.x).

```bash
dotnet build
```

```bash
dotnet test
```

```bash
dotnet run --project src/Doba.App
```

Useful flags (after `--` when using `dotnet run`):

| Flag | Effect |
|---|---|
| `--demo` | Start with the design's demo tasks and a fixed clock (15 Nov 2026, 22:26); nothing is read from or written to disk. |
| `--data <folder>` | Use another data folder instead of `%LOCALAPPDATA%\Doba`. |
| `--perf` | Print time-to-first-frame and working set to stdout. |
| `--exit-after-first-frame` | Quit as soon as the first frame is rendered (used with `--perf` in CI). |

## Publish (NativeAOT, Windows x64)

```bash
dotnet publish src/Doba.App -c Release -r win-x64 -o artifacts/doba-win-x64
```

The result is a single native `Doba.exe` plus the SQLite and Skia native libraries. CI
(`.github/workflows/ci.yml`) builds, tests and publishes this on `windows-latest` and uploads it as the
`doba-win-x64` artifact.

## Keyboard shortcuts

| Keys | Action |
|---|---|
| `N` | New task |
| `Ctrl` `Alt` `N` | Quick add from anywhere (global) |
| `Ctrl` `K` | Search |
| `G` `I` | Inbox |
| `G` `T` | Today |
| `1` `2` `3` `4` | Year / Month / Week / Day |
| `←` `→` | Previous / next period |
| `Space` | Complete the focused task |
| `Esc` | Close the open popover, then the window |
| `Enter` | Save in the task window |

Everything is also reachable with `Tab`, with a visible focus ring.

## Where data lives

`%LOCALAPPDATA%\Doba\` (e.g. `C:\Users\<you>\AppData\Local\Doba\`):

- `doba.db` — the SQLite database (tasks, tags, settings), WAL mode.
- `Backups\doba-yyyy-MM-dd.db` — one backup per day when *Daily backup* is on; the newest 7 are kept.

*Settings › Data* opens the folder and exports or imports everything as one JSON file.

## Project layout

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). The design sources are in [`docs/design/`](docs/design/);
decisions are logged in [`docs/DECISIONS.md`](docs/DECISIONS.md); verification status and the list of things that
need checking on a real Windows machine are in [`docs/HANDOFF.md`](docs/HANDOFF.md).

## Licence

Code: see [`LICENSE`](LICENSE). Fonts: Geist and Geist Mono, SIL Open Font License
([`src/Doba.App/Assets/Fonts/OFL.txt`](src/Doba.App/Assets/Fonts/OFL.txt)).
