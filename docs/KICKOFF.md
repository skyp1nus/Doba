# Doba — build brief for the lead architect

You are the **lead architect** for Doba, a personal daily planner / todo app for Windows desktop. You are running in a Claude Code cloud session with this repository checked out. Your job is to deliver the complete, working application described here: plan the work, delegate implementation to subagents, review what they return, integrate it, and keep going until the Definition of Done in §10 is met.

Nobody is watching this session, so there is no one to answer questions. When something is ambiguous, make the decision, record it, and continue.

> App name: **Doba**. The design files still show the working title "Todo" — use "Doba" everywhere a name appears (title bar, settings footer, data folder, assembly names).

---

## 1. The product

Doba is a local-only, single-user planner. It has an Inbox for undated tasks, a Today screen, and a Calendar that zooms Year → Month → Week → Day. A task has a title, optional notes, an optional date, an optional multi-day span, an optional time with a duration, a priority (none / low / medium / high / ASAP), and at most one tag. The UI is dark, calm, minimal, in English, with a precise motion language. No accounts, no sync, no telemetry, no network calls.

The owner is one developer who will use this every day and will read the code. He asked for two things above all: fidelity to the design and motion spec, and an app that is as fast and light as it can reasonably be.

## 2. Sources of truth — read these before planning

| File | What it is |
|---|---|
| `docs/design/design-reference.dc.html` | The full interactive design prototype. The markup holds exact layout, sizes, colours, typography and copy for every screen. The `<script>` block holds the reference behaviour: date math, sorting, grouping, multi-day lane layout, overflow rules, the task dialog logic, settings. |
| `docs/design/motion-spec.md` | Every animation: tokens, durations, easing, sequencing, the time wheel, and an acceptance checklist. Written in Ukrainian; the numbers and tokens are unambiguous. |
| `docs/design/screens/*.png` | Optional exported images of each screen. If the folder exists, use the images for visual comparison. If it does not, compare against the values in the prototype source. |

The prototype is a web mock-up, so treat it as a specification rather than code to port line by line. Its seed tasks and its fixed clock (`2026-11-15`, 22:26) are demo fixtures. It is drawn on a fixed 1600×1000 artboard; the real window is resizable, so anything that depends on available space (how many tasks fit in a day cell before "+N more") must take the measured size as input.

On visuals and motion, the design files win over this brief. Where they are silent, choose the simplest behaviour consistent with them and write one line in `docs/DECISIONS.md`.

## 3. The environment, and what follows from it

- This is an Ubuntu 24.04 x86_64 VM with no Windows and no display. The .NET 11 SDK should already be installed by the environment's setup script — check with `dotnet --list-sdks`. If it is missing, install it yourself first (`dotnet-install.sh --channel 11.0`, or `apt-get install dotnet-sdk-11.0`, adding `ppa:dotnet/backports` if needed).
- Network access is an allowlist: NuGet, GitHub and `dot.net` are reachable, most other sites are not. If a documentation site is blocked, read the library's source and docs on GitHub, or the XML docs in the NuGet package cache.
- You cannot launch the Windows app here. So everything you claim must be verifiable headlessly on Linux: unit tests, headless UI tests, and PNG screenshots rendered offscreen that you open and look at.
- Windows-only behaviour (tray icon, launch at startup, the global hotkey, the translucent backdrop, caption buttons) goes behind small interfaces with a Windows implementation and a no-op fallback, so the app still runs headless. Those parts are compiled here, exercised by CI on `windows-latest`, and listed for the owner under "Needs Windows verification".

## 4. Technical decisions (already made — don't reopen them)

- **Runtime:** .NET 11, C# 15, `net11.0`, nullable enabled, warnings as errors.
- **UI framework: Avalonia 12** (latest stable 12.x). Reason: it is the only .NET desktop UI stack that builds, runs and renders headlessly on this Linux VM, which is what makes autonomous verification possible; it also has render-thread composition animations and supports NativeAOT. WinUI 3 and WPF cannot be built or run here. Use compiled bindings everywhere. Build the look from your own control themes that match the design — do not ship a stock Fluent look.
- **MVVM:** CommunityToolkit.Mvvm source generators. A hand-written composition root instead of a DI container — the app is small and this keeps startup and AOT simple.
- **Storage:** SQLite through `Microsoft.Data.Sqlite` with hand-written SQL, WAL mode, schema version in `PRAGMA user_version`. All tasks are loaded into an in-memory store at startup; views read from memory; writes go to disk asynchronously but are flushed on close. Data lives in `%LOCALAPPDATA%\Doba`. Export/import is one JSON file via `System.Text.Json` source generation.
- **Publish target:** NativeAOT, `win-x64`, self-contained. Trimming and AOT analyzers on from the first commit. If a dependency blocks AOT, replace the dependency; only fall back to ReadyToRun + trimming as a last resort, with the reason in `DECISIONS.md`.
- **Tests:** xUnit v3, plus Avalonia's headless test platform with real Skia rendering for screenshots.
- **Fonts:** Geist and Geist Mono embedded as app resources (OFL licence; take them from the `geist` npm package or the `vercel/geist-font` GitHub repo), with the fallbacks named in the design.
- **Packages:** central package management (`Directory.Packages.props`), latest stable versions, looked up on NuGet rather than recalled.
- **Not in this project:** EF Core, ReactiveUI, a DI container, third-party UI component kits, web views.

Avalonia's API changed between 11 and 12 and your recollection of it may be out of date. Before relying on an API — composition animations, cubic-bezier easing, window transparency hints, the headless screenshot call — confirm it against the installed package or the Avalonia repo. Tell every subagent the same.

## 5. Architecture

```
Doba.sln
src/Doba.Core/          Pure domain. Task model, date math, settings model, and the view projections.
src/Doba.Storage/       SQLite repository, settings store, daily backup, JSON export/import.
src/Doba.App/           Avalonia app: shell, views, controls, theme tokens, Motion service, platform services.
tests/Doba.Core.Tests/
tests/Doba.Storage.Tests/
tests/Doba.App.Tests/   Headless UI tests and the screenshot harness.
docs/                   ARCHITECTURE.md, DECISIONS.md, PROGRESS.md, HANDOFF.md, design/, screenshots/
```

Rules that shape everything else:

1. **Projections live in Core.** All "what goes where" logic — grouping, sorting, lane assignment for multi-day spans, overflow counts, labels like "Day 2 of 3" — is pure functions in `Doba.Core` that return immutable view models. Views only render them. This mirrors the prototype's `weekData / monthData / yearData / dayData / inboxData / todayData` and makes the hard logic testable without any UI.
2. **Time is injected.** Use `TimeProvider`, never `DateTime.Now`. "Today", the now-line and the midnight rollover must be testable, and the UI must update when the day changes while the app is open.
3. **One source for every visual constant.** Colours, radii, spacing and font sizes come from theme tokens; every duration and easing comes from the `Motion` service described in the motion spec. No literal colour or duration in views.
4. **Platform behind interfaces**, as in §3.
5. **Changes are local.** Completing or editing a task updates the affected rows and counters; it must not rebuild the whole screen.

## 6. Scope of v1

Everything below is visible in the design prototype; read it there for exact appearance and rules.

**Shell.** Custom 40 px title bar (app mark and name, "Search tasks" button with `Ctrl K`, minimize / maximize / close). Sidebar 232 px, collapsible to a 56 px icon rail: Inbox and Today with open-task counts, Calendar, a Tags section with inline "new tag" input, Collapse, Settings. A sliding highlight marks the active section. Main panel with breadcrumb, title and subtitle, the Year / Month / Week / Day switch with sliding indicator, Today, previous / next, and "New task `N`".

**Tasks.** Checkbox to complete. Clicking the priority icon cycles none → low → medium → high → ASAP. Clicking the title opens the edit dialog. Clicking a tag in the sidebar filters by dimming tasks with other tags.

**Inbox.** Left column: undated tasks grouped ASAP / High / Medium / Low / No priority / Done. Right column "Scheduled": Overdue, Today, Tomorrow, each of the next 14 days that has tasks, then "Later".

**Today.** Summary line and progress bar; groups Overdue (with "→ Today"), In progress (multi-day), Scheduled, Anytime, Done; a schedule timeline on the right with the now-line.

**Calendar.** Year (twelve months with stats, progress and key items), Month (week rows with week numbers, day cells, multi-day bars, "+N more"), Week (first four days on the top row; last three days plus a dimmed preview of the next week's first day below; multi-day bars in lanes; per-day progress), Day (multi-day bars, task list, timeline). Clicking deeper zooms in from the click point; breadcrumbs zoom out.

**Task dialog** (new and edit). Title, notes, and four pills with popovers: Date (quick options, mini calendar, "lasts N days" 1–14), Time (any time / set time, the two-column wheel, duration), Priority, Tag. Enter saves, Esc closes the open popover first and then the dialog, Delete in edit mode. Create is disabled until the title is non-empty.

**Settings.** Replaces the sidebar with its own navigation. Appearance (accent: Mist / Sage / Sand / Gray; window transparency; theme; density), Calendar (week starts on; open on launch), Tasks (default priority; carry over unfinished tasks; completion sound), Windows (launch at startup; keep running in tray; global quick-add `Ctrl Alt N`), Data (folder, daily backup keeping 7 days, export / import), Shortcuts.

**Keyboard.** `N` new task · `Ctrl K` search · `G I` Inbox · `G T` Today · `1–4` Year / Month / Week / Day · `← →` previous / next · `Space` complete the focused task · `Esc` close. Everything must also be reachable by Tab with a visible focus state.

Gaps in the design that you should fill conservatively, logging each in `DECISIONS.md`:

- **Search** has only a button in the design. Build a small command-palette overlay in the style of the task dialog: substring match on titles across all tasks, arrow keys to move, Enter opens the task.
- **Light theme** is not designed. Ship dark only; keep the token structure ready for a second palette, and let "System" behave as Dark for now.
- **"Week starts on Sunday"** changes which days form the two rows of the Week view; generalise the rule instead of hard-coding Monday.
- **Window sizes** other than 1600×1000: the layout must stay usable from about 1100×700 upward.

Out of scope: sync, accounts, recurring tasks, reminders or notifications, focus mode, subtasks, attachments, localisation. Do not add features that are not in the design.

## 7. Performance budget

Treat these as requirements to measure, not slogans. Where a number can only be measured on Windows, add the instrumentation and report it honestly as unmeasured.

- **Startup:** first frame with real data within 400 ms cold for the AOT build on a mid-range PC. The app logs its own time-to-first-frame when started with `--perf`; CI prints it.
- **Memory:** aim for an idle working set under 120 MB. Report the measured number.
- **Projections:** each view projection finishes in under 5 ms against a 10,000-task store. Cover this with a test that has a generous margin for CI.
- **Animation:** 60 fps on the Week view with about 40 cards. Animate only opacity and transforms, on the composition/render thread. Progress bars animate a scale transform instead of width. Never rebuild the visual tree during a content transition. The blur in the zoom transition is optional — drop it if it costs frames.
- **Lists:** virtualise anything that can grow without bound (Inbox).
- **No reflection-based anything** on hot paths or at startup: compiled bindings, source-generated JSON and MVVM.

## 8. How you work

**Your role.** You own the architecture document, the contracts (public interfaces, view-model records, token and Motion names), the scaffold, `PROGRESS.md`, `DECISIONS.md`, integration, and every commit. You write code yourself only for the scaffold, the contracts and small integration fixes. Features go to subagents, which keeps your own context free for coordinating a long build.

**Subagents.** Use implementer subagents for work packages and separate reviewer subagents — always a fresh one that did not write the code — for code review and for visual review.

**Phases.**

0. *Bootstrap.* Verify the SDK. Read both design files fully. Write `docs/ARCHITECTURE.md`, `docs/PROGRESS.md` (a checkbox list of work packages, each with acceptance criteria) and `CLAUDE.md` (build and test commands, conventions, where the design files are — subagents load it automatically). Scaffold the solution with analyzers, central package versions, a CI workflow, and a headless screenshot test that passes. Port the prototype's seed tasks and fixed clock as a `DemoData` fixture so screenshots are comparable with the design. Commit and push.
1. *Foundations* — in parallel: Core (model, date math, projections, tests) · Storage · design system in the app (tokens, fonts, control themes for buttons, checkbox, priority icon, segmented switch, toggle, pills, kbd, scrollbars) plus the `Motion` service · the shell (window chrome, sidebar, header, navigation state).
2. *Screens* — in parallel once contracts are stable: Inbox · Today · Week · Month · Year · Day · task dialog with popovers and the time wheel · Settings.
3. *Motion pass.* Wire every transition in the motion spec through `Motion`; make rapid repeated clicks safe; honour the OS "animations off" setting.
4. *Platform and remaining features.* Tray, autostart, global hotkey, backdrop transparency, backup, export / import, search, shortcuts, carry-over, midnight rollover.
5. *Hardening.* Performance pass against §7, zero AOT/trim warnings on publish, a full independent review, README and handoff.

**Delegating well.** A subagent starts with none of your context, so every brief is self-contained:

```
Goal            one sentence, and why it matters
Read first      exact files and sections (design reference lines, motion-spec section, ARCHITECTURE.md part)
You own         the paths this agent may create or edit
Do not touch    everything else, especially contracts
Contracts       the interfaces / records / tokens to conform to
Done when       concrete acceptance criteria, including tests to add
Verify with     the exact commands to run before reporting
Report back     what changed, decisions made, anything unfinished or unverified — stated plainly
```

- Run packages in parallel only when their file ownership does not overlap; three to five at a time is a good batch. Use worktree isolation if the tool offers it. Otherwise have each subagent build and test only its own project, and run the solution-wide build yourself after integrating — concurrent builds of the same project corrupt each other's output.
- When a subagent returns, read the diff, not just its report. Run the gates. If it falls short, send it back once or twice with specific feedback; after that, give a narrower package to a fresh agent.
- Commit after each package that passes, with a message that says what and why. Push regularly so the work survives the VM being reclaimed.

**Gates for every package.**

1. `dotnet build -warnaserror` is clean, including trim and AOT analyzers.
2. `dotnet test` is green.
3. For UI work: regenerate screenshots at 1600×1000 with `DemoData`, save them to `docs/screenshots/`, and have a reviewer subagent compare each with the matching screen in the design (structure, spacing, sizes, colours, typography, copy). Fix what it finds.
4. No literal colours or durations outside tokens and `Motion` (a grep-based test is fine).

## 9. Staying on course

- **`docs/PROGRESS.md` is the state of the project.** Tick items as they pass their gates and add a line of notes. After a context compaction or a resumed session, re-read this file, `CLAUDE.md`, `docs/ARCHITECTURE.md`, `docs/PROGRESS.md` and `git log --oneline -20`, then carry on with the next open item.
- **Do not end your turn while `PROGRESS.md` has open items.** Finishing a phase is not a stopping point, and progress belongs in `PROGRESS.md`, not in a summary message. A Stop hook in this repository will send you back to work if you try.
- **No questions to the owner.** Pick the option closest to the design files, log it in `DECISIONS.md`, move on.
- **When one thing is blocked,** note it under "Blocked" in `PROGRESS.md` with what you tried, take the next unblocked package, and return to it later. Only if everything that remains is blocked by something outside your control — for example the SDK cannot be installed by any route — write `docs/BLOCKED.md` saying exactly what the owner must do, commit, push, and then stop.
- **Never fake green.** Do not delete or skip tests, weaken assertions, or tick an item whose gates have not passed. If something cannot be verified on Linux, list it under "Needs Windows verification" instead of claiming it works.
- **Stay in scope.** §6 and the design define v1.

## 10. Definition of Done

- Every item in `PROGRESS.md` is ticked, and `dotnet build` and `dotnet test` pass on a fresh clone.
- Each screen has an accepted screenshot in `docs/screenshots/`: Inbox, Today, Year, Month, Week, Day, the task dialog with each of its four popovers open, all six Settings tabs, the collapsed sidebar, and compact density.
- Every item of the motion spec's acceptance checklist is covered by a test or by a recorded review.
- A GitHub Actions workflow on `windows-latest` builds, tests, publishes the NativeAOT `win-x64` build and uploads it as an artifact. If you can see workflow runs, iterate until it is green.
- `README.md` explains what Doba is, how to build, run and publish it, the shortcuts, and where data lives. `docs/HANDOFF.md` records what was verified and how, the "Needs Windows verification" list with exact manual steps, the measured performance numbers, and known gaps.
- The final state is committed and pushed, with a pull request (or branch summary) describing the result.

Start with Phase 0 now.
