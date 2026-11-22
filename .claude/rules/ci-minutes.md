# Save GitHub Actions minutes

Doba is checked on the Windows PC, not in GitHub Actions. CI runs once per release PR, as the last gate. The shared
rule for all the owner's repos:
[scl-infra `docs/infra/ci-cd.md`, "Spending Actions minutes"](https://github.com/skyp1nus/scl-infra/blob/dev/docs/infra/ci-cd.md#spending-actions-minutes).

## What starts CI

`.github/workflows/ci.yml` runs only on:

- a pull request into `main` (paths `docs/**`, `**/*.md`, `tools/**` are ignored), and
- `workflow_dispatch`, kept for the owner.

A plain push to `dev` or to any other branch starts nothing.

Gotcha: while a PR into `main` is open, every push to its branch (`dev`) starts a run, even for a docs-only commit.
`paths-ignore` on `pull_request` looks at the whole diff of the PR, not at the last commit.

## Never

- `gh workflow run`, the "Run workflow" button, or any `workflow_dispatch` to check something halfway. Only the owner
  starts a manual run.
- Push to `dev` while the PR into `main` is open, unless the push is the fix for that PR.
- Push intermediate commits, or open the PR "to see what CI says".
- Edit `ci.yml` to add triggers (push, other branches, schedules) or extra jobs without the owner's word.

## Before opening the PR into `main`

Finish everything first, so the PR needs exactly one push:

1. All commits are on `dev`, including the `docs/PROGRESS.md` and `docs/HANDOFF.md` ticks and the version bump in
   `Directory.Build.props`.
2. The local checks pass on this PC (commands in `CLAUDE.md` › Commands), each with 0 warnings:
   - `dotnet build -c Release -warnaserror`
   - `dotnet test -c Release --no-build`
   - `dotnet publish src/Doba.App -c Release -r win-x64 -o artifacts\doba-win-x64 -warnaserror`
   - `dotnet publish src/Doba.Mcp -c Release -r win-x64 -o artifacts\doba-win-x64 -warnaserror`
   - `$env:DOBA_MCP_EXE="$PWD\artifacts\doba-win-x64\Doba.Mcp.exe"; dotnet test --project tests/Doba.Bridge.Tests`
3. The owner has checked the build when the change touches native Windows behaviour (tray, hotkey, window chrome).

Then push `dev` once and open the PR. Write in its description what was run locally.

## If CI fails

Reproduce it locally, fix every cause, and push all the fixes in one push. Do not push one fix at a time to see whether
it helps.

## Release

The release reuses the `doba-win-x64` artifact of the PR's green run (`gh run download`). It needs no other run.
