<#
.SYNOPSIS
  Measures Doba against the performance budget (docs/KICKOFF.md section 7) with the NativeAOT build.

.DESCRIPTION
  1. Publishes the NativeAOT build to artifacts\doba-win-x64 (skip with -SkipPublish).
  2. Writes the test databases (348 demo-like tasks, 10,000 tasks) to artifacts\perf\data through the app's own
     Storage API (the PerfDatabaseTests test with DOBA_PERF_SEED_DIR set).
  3. Startup: -Runs launches each of --demo, the 348-task database, the 10,000-task database and a fresh empty
     folder (the run creates the database), interleaved, with --perf --exit-after-first-frame. Every --data run gets
     a fresh copy of its database under artifacts\perf\run. The first launch after a publish is reported on its own
     (the closest thing to a cold start that can be produced on demand).
  4. Idle memory: --perf-idle-seconds for --demo and the 10,000-task database.
  5. Frames: --perf-scenario week (Week view, next/previous, Inbox and back, Month and back) for --demo and the
     10,000-task database.
  Prints one summary table (median and worst) and saves it under artifacts\perf\.

  Safe on a working machine: the app only runs with --demo or --data <folder under artifacts\>, never against
  %LOCALAPPDATA%\Doba or the Run key. A --data launch does register the Ctrl+Alt+N hotkey and shows the tray icon
  while it runs (as any --data launch does); both are removed when it exits. Only processes this script started are
  ever stopped (on a timeout), and none is left running.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\perf.ps1
  powershell -ExecutionPolicy Bypass -File scripts\perf.ps1 -SkipPublish -Runs 5 -SkipScenario
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 100)][int]$Runs = 10,
    [ValidateRange(1, 20)][int]$IdleRuns = 3,
    [ValidateRange(1, 600)][int]$IdleSeconds = 5,
    [ValidateRange(1, 20)][int]$ScenarioRuns = 2,
    [ValidateRange(1, 100)][int]$ScenarioRounds = 5,
    [ValidateRange(10, 3600)][int]$TimeoutSeconds = 180,
    [switch]$SkipPublish,
    [switch]$SkipIdle,
    [switch]$SkipScenario
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
$exe = Join-Path $artifacts 'doba-win-x64\Doba.exe'
$work = Join-Path $artifacts 'perf'
$seed = Join-Path $work 'data'
$runRoot = Join-Path $work 'run'
$inv = [System.Globalization.CultureInfo]::InvariantCulture

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:MSBUILDDISABLENODEREUSE = '1'

function Write-Step([string]$text) { Write-Host "== $text" -ForegroundColor Cyan }

function Invoke-Checked([string]$what, [scriptblock]$block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

# The app may only ever run on --demo or on a --data folder under artifacts\: anything else would open
# %LOCALAPPDATA%\Doba and reconcile the real Run key. Every argument must be a single flag, a number or that folder.
function Assert-SafeArguments([string[]]$arguments) {
    $root = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
    $data = [Array]::IndexOf($arguments, '--data')
    $dataOk = $data -ge 0 -and $data + 1 -lt $arguments.Count -and
        [System.IO.Path]::GetFullPath($arguments[$data + 1]).StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
    if (-not (($arguments -contains '--demo') -or $dataOk)) {
        throw "Refusing to start Doba without --demo or --data <folder under $root>: $($arguments -join ' | ')"
    }
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        $a = $arguments[$i]
        if ($dataOk -and $i -eq $data + 1) { continue }
        if ($a -notmatch '^(--[a-z-]+|\d+|week)$' -or $a -in @('--minimized', '--selfcheck')) {
            throw "Refusing to start Doba with argument '$a' ($($arguments -join ' | '))"
        }
    }
}

# Runs Doba.exe with $arguments, returns its stdout lines. Stops (only) this process if it outlives the timeout.
function Invoke-Doba([string[]]$arguments) {
    Assert-SafeArguments $arguments
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.WorkingDirectory = $work
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Arguments = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $process = [System.Diagnostics.Process]::Start($psi)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Doba $($psi.Arguments) did not exit within $TimeoutSeconds s (stopped)."
        }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Doba $($psi.Arguments) exited with $($process.ExitCode): $($stderr.Result)"
        }
        return ($stdout.Result -split "`r?`n" | Where-Object { $_ })
    }
    finally {
        if (-not $process.HasExited) { $process.Kill() }
        $process.Dispose()
    }
}

# "doba-perf a=1 b=x" -> @{ a = '1'; b = 'x' } for the first line starting with $prefix.
function Get-Fields([string[]]$lines, [string]$prefix) {
    $line = $lines | Where-Object { $_.StartsWith($prefix) } | Select-Object -First 1
    if (-not $line) { throw "No '$prefix' line in: $($lines -join ' | ')" }
    $fields = @{}
    foreach ($token in $line.Split(' ')) {
        $eq = $token.IndexOf('=')
        if ($eq -gt 0) { $fields[$token.Substring(0, $eq)] = $token.Substring($eq + 1) }
    }
    return $fields
}

function Get-Number($fields, [string]$name) {
    $value = 0.0
    if ($fields.ContainsKey($name) -and [double]::TryParse($fields[$name], [System.Globalization.NumberStyles]::Float, $inv, [ref]$value)) {
        return $value
    }
    return [double]::NaN
}

function Get-Median([double[]]$values) {
    $sorted = @($values | Where-Object { -not [double]::IsNaN($_) } | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $mid = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$mid] }
    return ($sorted[$mid - 1] + $sorted[$mid]) / 2
}

function Get-Worst([double[]]$values) {
    $sorted = @($values | Where-Object { -not [double]::IsNaN($_) } | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    return $sorted[-1]
}

function F([double]$value, [string]$format = 'F0') {
    if ([double]::IsNaN($value)) { return 'n/a' }
    return $value.ToString($format, $inv)
}

# A fresh copy of a seeded database (or an empty folder for 'fresh') to launch against.
function New-RunFolder([string]$set, [int]$index) {
    $folder = Join-Path $runRoot ("{0}-{1}" -f $set, $index)
    if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
    New-Item -ItemType Directory -Force $folder | Out-Null
    if ($set -ne 'fresh') { Copy-Item (Join-Path $seed "$set\doba.db") $folder }
    return $folder
}

function Get-LaunchArgs([string]$set, [int]$index) {
    if ($set -eq 'demo') { return @('--demo') }
    return @('--data', (New-RunFolder $set $index))
}

New-Item -ItemType Directory -Force $work | Out-Null
Push-Location $repo
try {
    $published = $false
    if (-not $SkipPublish) {
        Write-Step 'Publishing the NativeAOT build'
        Invoke-Checked 'dotnet publish' { dotnet publish src/Doba.App -c Release -r win-x64 -o (Join-Path $artifacts 'doba-win-x64') -warnaserror --nologo -v quiet }
        $published = $true
    }
    if (-not (Test-Path $exe)) { throw "No published build at $exe (run without -SkipPublish)." }

    Write-Step 'Writing the 348- and 10,000-task databases'
    $env:DOBA_PERF_SEED_DIR = $seed
    try {
        Invoke-Checked 'seeding the databases' { dotnet test --project tests/Doba.App.Tests -v quiet -- --filter-method '*Perf_databases_hold_every_task_and_load_quickly' | Out-Null }
    }
    finally {
        Remove-Item Env:DOBA_PERF_SEED_DIR -ErrorAction SilentlyContinue
    }

    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $os = Get-CimInstance Win32_OperatingSystem
    $video = Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentRefreshRate } | Select-Object -First 1
    $refresh = if ($video) { $video.CurrentRefreshRate } else { '?' }
    $report = New-Object System.Collections.Generic.List[string]
    $report.Add("Doba perf  $(Get-Date -Format 'yyyy-MM-dd HH:mm')  commit $(git rev-parse --short HEAD)")
    $report.Add("Machine    $cpu, $([math]::Round($os.TotalVisibleMemorySize / 1MB)) GB, Windows $($os.Version) (build $($os.BuildNumber)), display $refresh Hz")
    $report.Add("Build      $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)")
    $report.Add('')

    # ---- Startup ---------------------------------------------------------------------------------------------
    $sets = @('demo', 'db-348', 'db-10k', 'fresh')
    $startup = @{}
    foreach ($set in $sets) { $startup[$set] = New-Object System.Collections.Generic.List[object] }
    $firstAfterPublish = $null
    Write-Step "Startup: $Runs runs each of $($sets -join ', ')"
    for ($i = 0; $i -lt $Runs; $i++) {
        foreach ($set in $sets) {
            $fields = Get-Fields (Invoke-Doba (@(Get-LaunchArgs $set $i) + @('--perf', '--exit-after-first-frame'))) 'doba-perf time-to-first-frame-ms='
            if ($published -and $null -eq $firstAfterPublish) { $firstAfterPublish = $fields; continue }
            $startup[$set].Add($fields)
        }
        Write-Host -NoNewline '.'
    }
    Write-Host ''

    $report.Add('Startup (warm; ms; median / worst)  Main->rendered = first frame with data drawn; +pre-Main = from process start')
    $report.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-15} {5,9} {6,12} {7,14} {8,9} {9,10} {10,6}' -f 'set', 'runs', 'Main->rendered', 'process->rend.', 'Main->callback', 'pre-Main', 'Main->shown', 'shown->rend.', 'WS MB', 'priv. MB', 'tasks'))
    foreach ($set in $sets) {
        $rows = $startup[$set]
        $col = { param($name) [double[]]@($rows | ForEach-Object { Get-Number $_ $name }) }
        $rendered = & $col 'frame-rendered-ms'
        $total = & $col 'from-process-start-ms'
        $callback = & $col 'time-to-first-frame-ms'
        $report.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-15} {5,9} {6,12} {7,14} {8,9} {9,10} {10,6}' -f $set, $rows.Count,
            ((F (Get-Median $rendered)) + ' / ' + (F (Get-Worst $rendered))),
            ((F (Get-Median $total)) + ' / ' + (F (Get-Worst $total))),
            ((F (Get-Median $callback)) + ' / ' + (F (Get-Worst $callback))),
            (F (Get-Median (& $col 'process-to-main-ms'))),
            (F (Get-Median (& $col 'main-to-shown-ms'))),
            (F (Get-Median (& $col 'shown-to-rendered-ms'))),
            (F (Get-Median (& $col 'working-set-mb')) 'F1'),
            (F (Get-Median (& $col 'private-mb')) 'F1'),
            ($rows | Select-Object -First 1 | ForEach-Object { $_['tasks'] })))
    }
    if ($firstAfterPublish) {
        $report.Add(('first launch after publish (demo; cold proxy): Main->rendered {0} ms, from process start {1} ms' -f
            (F (Get-Number $firstAfterPublish 'frame-rendered-ms')), (F (Get-Number $firstAfterPublish 'from-process-start-ms'))))
    }
    $report.Add('')

    # ---- Idle memory -----------------------------------------------------------------------------------------
    if (-not $SkipIdle) {
        Write-Step "Idle memory: $IdleRuns runs of $IdleSeconds s each for demo, db-10k"
        $report.Add("Idle memory after $IdleSeconds s on the launch screen (MB; median / worst)")
        $report.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-12}' -f 'set', 'runs', 'working set', 'private bytes', 'managed heap'))
        foreach ($set in @('demo', 'db-10k')) {
            $rows = @(for ($i = 0; $i -lt $IdleRuns; $i++) {
                    Get-Fields (Invoke-Doba (@(Get-LaunchArgs $set (100 + $i)) + @('--perf-idle-seconds', "$IdleSeconds"))) 'doba-perf idle-seconds='
                })
            $ws = [double[]]@($rows | ForEach-Object { Get-Number $_ 'working-set-mb' })
            $pb = [double[]]@($rows | ForEach-Object { Get-Number $_ 'private-mb' })
            $mh = [double[]]@($rows | ForEach-Object { Get-Number $_ 'managed-heap-mb' })
            $report.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-12}' -f $set, $rows.Count,
                ((F (Get-Median $ws) 'F1') + ' / ' + (F (Get-Worst $ws) 'F1')),
                ((F (Get-Median $pb) 'F1') + ' / ' + (F (Get-Worst $pb) 'F1')),
                (F (Get-Median $mh) 'F1')))
        }
        $report.Add('')
    }

    # ---- Week scenario ---------------------------------------------------------------------------------------
    if (-not $SkipScenario) {
        Write-Step "Week scenario: $ScenarioRuns runs x $ScenarioRounds rounds for demo, db-10k"
        $report.Add("Week scenario ($ScenarioRounds rounds x 6 navigations; ms; render = render-thread frames, ui = UI-thread frames)")
        $report.Add(('{0,-8} {1,-14} {2,-24} {3,-22} {4,-18} {5,-16} {6,-12}' -f 'set', 'cards+bars', 'render p50/p95/max', 'render >33.3/>50 ms', 'ui max/>50 ms', 'GCs (pause ms)', 'live heap MB'))
        foreach ($set in @('demo', 'db-10k')) {
            for ($i = 0; $i -lt $ScenarioRuns; $i++) {
                $f = Get-Fields (Invoke-Doba (@(Get-LaunchArgs $set (200 + $i)) + @('--perf-scenario', 'week', '--perf-rounds', "$ScenarioRounds"))) 'doba-perf scenario=week'
                $report.Add(('{0,-8} {1,-14} {2,-24} {3,-22} {4,-18} {5,-16} {6,-12}' -f $set,
                    ($f['cards'] + '+' + $f['bars']),
                    ($f['render-p50-ms'] + ' / ' + $f['render-p95-ms'] + ' / ' + $f['render-max-ms']),
                    ($f['render-over-33.3ms'] + ' / ' + $f['render-over-50ms']),
                    ($f['ui-max-ms'] + ' / ' + $f['ui-over-50ms']),
                    ([string]([int]$f['gc0'] + [int]$f['gc1'] + [int]$f['gc2']) + ' (' + $f['gc-pause-ms'] + ')'),
                    $f['live-heap-mb']))
            }
        }
        $report.Add('')
    }

    $file = Join-Path $work ("results-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $report | Set-Content -Encoding UTF8 $file
    Write-Host ''
    $report | ForEach-Object { Write-Host $_ }
    Write-Host "Saved to $file"
}
finally {
    Pop-Location
    # Nothing this script started may outlive it (Invoke-Doba already waits for or stops each process).
}
