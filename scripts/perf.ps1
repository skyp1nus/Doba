<#
.SYNOPSIS
  Measures Doba against the performance budget (docs/KICKOFF.md section 7) with the NativeAOT build.

.DESCRIPTION
  1. Publishes the NativeAOT build to artifacts\doba-win-x64 (skip with -SkipPublish).
  2. Writes the test databases to artifacts\perf\data through the app's own Storage API (the PerfDatabaseTests test
     with DOBA_PERF_SEED_DIR set): 348 demo-like tasks (db-348), 10,000 tasks (db-10k), and 10,000 tasks plus a dense
     current week in compact density (db-dense-compact: the Week view filled to capacity, about 40 cards and bars at
     1600x1000 - the load of the "60 fps on the Week view with about 40 cards" budget).
  3. Startup: -Runs launches each of --demo, db-348, db-10k and a fresh empty folder (the run creates the database),
     interleaved, with --perf --exit-after-first-frame. Every --data run gets a fresh copy of its database under
     artifacts\perf\run. ALL OF THESE ARE WARM STARTS: Doba.exe, its DLLs and the database are in the OS file cache.
     Then -ColdExeRuns launches of --demo, each from a new copy of the published folder written with unbuffered I/O
     (robocopy /J), so Doba.exe and e_sqlite3.dll come from disk (and the antivirus scans the new exe) as on a first
     launch after install, while Windows' own DLLs stay warm: a partly cold start, the closest that can be produced
     on demand. A true cold start needs a reboot: see -PrepareCold / -Cold below.
  4. Idle memory: --perf-idle-seconds for --demo, db-348 and db-10k.
  5. Frames: --perf-scenario week (Week view: next/previous, Inbox and back, Month and back; then the New task dialog
     opened and closed with the backdrop blur and without it) for --demo, db-10k and db-dense-compact.
  Prints one summary table (median and worst) and saves it under artifacts\perf\.

  True cold start (after a reboot), in two steps:
    a. scripts\perf.ps1 -PrepareCold   publishes (unless -SkipPublish), seeds the databases and prepares the folders
                                       the cold launches use, then stops. Start nothing of Doba after it.
    b. Reboot, sign in, wait about two minutes for the desktop to settle, start nothing else, then run
       scripts\perf.ps1 -Cold          launches --demo once and the prepared db-10k copy once (both cold), then
                                       each again (warm) for comparison; prints and saves the lines, and stops.

  Safe on a working machine: the app only runs with --demo or --data <folder under artifacts\>, never against
  %LOCALAPPDATA%\Doba or the Run key. A --data launch does register the Ctrl+Alt+N hotkey and shows the tray icon
  while it runs (as any --data launch does); both are removed when it exits. Only processes this script started are
  ever stopped (on a timeout), and none is left running.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\perf.ps1
  powershell -ExecutionPolicy Bypass -File scripts\perf.ps1 -SkipPublish -Runs 5 -SkipScenario
  powershell -ExecutionPolicy Bypass -File scripts\perf.ps1 -PrepareCold     (then reboot, then: -Cold)
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 100)][int]$Runs = 10,
    [ValidateRange(0, 20)][int]$ColdExeRuns = 3,
    [ValidateRange(1, 20)][int]$IdleRuns = 3,
    [ValidateRange(1, 600)][int]$IdleSeconds = 5,
    [ValidateRange(1, 20)][int]$ScenarioRuns = 2,
    [ValidateRange(1, 100)][int]$ScenarioRounds = 5,
    [ValidateRange(10, 3600)][int]$TimeoutSeconds = 180,
    [switch]$SkipPublish,
    [switch]$SkipIdle,
    [switch]$SkipScenario,
    [switch]$PrepareCold,
    [switch]$Cold
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
$exe = Join-Path $artifacts 'doba-win-x64\Doba.exe'
$work = Join-Path $artifacts 'perf'
$seed = Join-Path $work 'data'
$runRoot = Join-Path $work 'run'
$coldRoot = Join-Path $work 'cold'
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$script:launchExe = $exe

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:MSBUILDDISABLENODEREUSE = '1'

function Write-Step([string]$text) { Write-Host "== $text" -ForegroundColor Cyan }

function Invoke-Checked([string]$what, [scriptblock]$block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

function Test-UnderArtifacts([string]$path) {
    $root = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
    return [System.IO.Path]::GetFullPath($path).StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
}

# The app may only ever run on --demo or on a --data folder under artifacts\: anything else would open
# %LOCALAPPDATA%\Doba and reconcile the real Run key. Every argument must be a single flag, a number or that folder.
function Assert-SafeArguments([string[]]$arguments) {
    $data = [Array]::IndexOf($arguments, '--data')
    $dataOk = $data -ge 0 -and $data + 1 -lt $arguments.Count -and (Test-UnderArtifacts $arguments[$data + 1])
    if (-not (($arguments -contains '--demo') -or $dataOk)) {
        throw "Refusing to start Doba without --demo or --data <folder under $artifacts>: $($arguments -join ' | ')"
    }
    for ($i = 0; $i -lt $arguments.Count; $i++) {
        $a = $arguments[$i]
        if ($dataOk -and $i -eq $data + 1) { continue }
        if ($a -notmatch '^(--[a-z-]+|\d+|week)$' -or $a -in @('--minimized', '--selfcheck')) {
            throw "Refusing to start Doba with argument '$a' ($($arguments -join ' | '))"
        }
    }
    if (-not (Test-UnderArtifacts $script:launchExe)) { throw "Refusing to start a Doba.exe outside $artifacts`: $script:launchExe" }
}

# Runs Doba.exe with $arguments, returns its stdout lines. Stops (only) this process if it outlives the timeout.
function Invoke-Doba([string[]]$arguments) {
    Assert-SafeArguments $arguments
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:launchExe
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

# Runs a Doba.exe other than the published one (a copy under artifacts\ only).
function Invoke-DobaFrom([string]$path, [string[]]$arguments) {
    $script:launchExe = $path
    try { return Invoke-Doba $arguments }
    finally { $script:launchExe = $exe }
}

# A copy of the published folder written with unbuffered I/O (robocopy /J), so its files are not in the OS file cache.
function New-ColdCopy([string]$name) {
    $folder = Join-Path $coldRoot $name
    if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }
    & robocopy (Split-Path -Parent $exe) $folder /E /J /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit code $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
    return (Join-Path $folder 'Doba.exe')
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

function V($fields, [string]$name) { if ($fields.ContainsKey($name)) { return $fields[$name] } else { return 'n/a' } }

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

function Get-MachineLines {
    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $os = Get-CimInstance Win32_OperatingSystem
    $video = Get-CimInstance Win32_VideoController | Where-Object { $_.CurrentRefreshRate } | Select-Object -First 1
    $refresh = if ($video) { $video.CurrentRefreshRate } else { '?' }
    $gpu = if ($video) { $video.Name } else { '?' }
    return @(
        "Doba perf  $(Get-Date -Format 'yyyy-MM-dd HH:mm')  commit $(git rev-parse --short HEAD)",
        "Machine    $cpu, $([math]::Round($os.TotalVisibleMemorySize / 1MB)) GB, $gpu, Windows $($os.Version) (build $($os.BuildNumber)), display $refresh Hz",
        "Build      $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)")
}

function Format-OneStart([string]$label, $fields) {
    return ('{0,-34} Main->rendered {1,5} ms   from process start {2,5} ms   pre-Main {3,4} ms' -f $label,
        (F (Get-Number $fields 'frame-rendered-ms')), (F (Get-Number $fields 'from-process-start-ms')), (F (Get-Number $fields 'process-to-main-ms')))
}

function Save-Report($report) {
    $file = Join-Path $work ("results-{0}.txt" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $report | Set-Content -Encoding UTF8 $file
    Write-Host ''
    $report | ForEach-Object { Write-Host $_ }
    Write-Host "Saved to $file"
}

$coldDemoExe = Join-Path $coldRoot 'reboot\Doba.exe'
$coldData = Join-Path $coldRoot 'reboot-db-10k'

New-Item -ItemType Directory -Force $work | Out-Null
Push-Location $repo
try {
    # ---- True cold start, after a reboot (prepared by -PrepareCold) ------------------------------------------
    if ($Cold) {
        if (-not (Test-Path $coldDemoExe) -or -not (Test-Path (Join-Path $coldData 'doba.db'))) {
            throw 'Nothing prepared: run scripts\perf.ps1 -PrepareCold first, then reboot.'
        }
        $boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
        $report = New-Object System.Collections.Generic.List[string]
        Get-MachineLines | ForEach-Object { $report.Add($_) }
        $report.Add("Booted     $boot ($([math]::Round(((Get-Date) - $boot).TotalMinutes, 1)) min before this run)")
        $report.Add('')
        $report.Add('Startup after a reboot (first launch of each = COLD; the repeat = warm)')
        $coldDataCopy = Join-Path $runRoot 'reboot-db-10k-warm'
        $first = Get-Fields (Invoke-DobaFrom $coldDemoExe @('--demo', '--perf', '--exit-after-first-frame')) 'doba-perf time-to-first-frame-ms='
        $report.Add((Format-OneStart 'demo    COLD (first after reboot)' $first))
        $firstDb = Get-Fields (Invoke-DobaFrom $coldDemoExe @('--data', $coldData, '--perf', '--exit-after-first-frame')) 'doba-perf time-to-first-frame-ms='
        $report.Add((Format-OneStart 'db-10k  database COLD (exe warm now)' $firstDb))
        $again = Get-Fields (Invoke-DobaFrom $coldDemoExe @('--demo', '--perf', '--exit-after-first-frame')) 'doba-perf time-to-first-frame-ms='
        $report.Add((Format-OneStart 'demo    warm repeat' $again))
        if (Test-Path $coldDataCopy) { Remove-Item -Recurse -Force $coldDataCopy }
        New-Item -ItemType Directory -Force $coldDataCopy | Out-Null
        Copy-Item (Join-Path $seed 'db-10k\doba.db') $coldDataCopy
        $againDb = Get-Fields (Invoke-DobaFrom $coldDemoExe @('--data', $coldDataCopy, '--perf', '--exit-after-first-frame')) 'doba-perf time-to-first-frame-ms='
        $report.Add((Format-OneStart 'db-10k  warm repeat' $againDb))
        Save-Report $report
        return
    }

    $published = $false
    if (-not $SkipPublish) {
        Write-Step 'Publishing the NativeAOT build'
        Invoke-Checked 'dotnet publish' { dotnet publish src/Doba.App -c Release -r win-x64 -o (Join-Path $artifacts 'doba-win-x64') -warnaserror --nologo -v quiet }
        $published = $true
    }
    if (-not (Test-Path $exe)) { throw "No published build at $exe (run without -SkipPublish)." }

    Write-Step 'Writing the test databases'
    $env:DOBA_PERF_SEED_DIR = $seed
    try {
        Invoke-Checked 'seeding the databases' { dotnet test --project tests/Doba.App.Tests -v quiet -- --filter-method '*Perf_databases_hold_every_task_and_load_quickly' | Out-Null }
    }
    finally {
        Remove-Item Env:DOBA_PERF_SEED_DIR -ErrorAction SilentlyContinue
    }

    if ($PrepareCold) {
        Write-Step 'Preparing the cold-start folders'
        $null = New-ColdCopy 'reboot'
        if (Test-Path $coldData) { Remove-Item -Recurse -Force $coldData }
        New-Item -ItemType Directory -Force $coldData | Out-Null
        Copy-Item (Join-Path $seed 'db-10k\doba.db') $coldData
        Write-Host "Prepared $coldDemoExe and $coldData."
        Write-Host 'Now reboot, sign in, wait about two minutes, start nothing else, and run: scripts\perf.ps1 -Cold'
        return
    }

    $report = New-Object System.Collections.Generic.List[string]
    Get-MachineLines | ForEach-Object { $report.Add($_) }
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

    $report.Add('Startup, WARM (Doba.exe, its DLLs and the database already in the OS file cache; ms; median / worst)')
    $report.Add('  Main->rendered = first frame with data drawn; process->rend. = the same from process start (includes pre-Main)')
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
        $report.Add((Format-OneStart 'first launch after publish (demo; WARM: publish just wrote the files)' $firstAfterPublish))
    }

    if ($ColdExeRuns -gt 0) {
        Write-Step "Partly cold: $ColdExeRuns launches of --demo from an uncached copy of the build"
        $report.Add("Startup, PARTLY COLD (--demo from a new copy of the build written with unbuffered I/O: Doba.exe and e_sqlite3.dll")
        $report.Add('  read from disk and scanned by the antivirus as on a first launch after install; Windows DLLs warm)')
        for ($i = 0; $i -lt $ColdExeRuns; $i++) {
            $copy = New-ColdCopy ("exe-{0}" -f $i)
            $fields = Get-Fields (Invoke-DobaFrom $copy @('--demo', '--perf', '--exit-after-first-frame')) 'doba-perf time-to-first-frame-ms='
            $report.Add((Format-OneStart ("demo, uncached copy #{0}" -f ($i + 1)) $fields))
            $fields = Get-Fields (Invoke-DobaFrom $copy @('--demo', '--perf', '--exit-after-first-frame')) 'doba-perf time-to-first-frame-ms='
            $report.Add((Format-OneStart '  same copy again (warm)' $fields))
            Remove-Item -Recurse -Force (Split-Path -Parent $copy)
        }
    }
    $report.Add('A true cold start (after a reboot) is not in this run: scripts\perf.ps1 -PrepareCold, reboot, scripts\perf.ps1 -Cold.')
    $report.Add('')

    # ---- Idle memory -----------------------------------------------------------------------------------------
    if (-not $SkipIdle) {
        $idleSets = @('demo', 'db-348', 'db-10k')
        Write-Step "Idle memory: $IdleRuns runs of $IdleSeconds s each for $($idleSets -join ', ')"
        $report.Add("Idle memory after $IdleSeconds s on the launch screen (MB; median / worst)")
        $report.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-12} {5,-12}' -f 'set', 'runs', 'working set', 'private bytes', 'managed heap', 'GC committed'))
        foreach ($set in $idleSets) {
            $rows = @(for ($i = 0; $i -lt $IdleRuns; $i++) {
                    Get-Fields (Invoke-Doba (@(Get-LaunchArgs $set (100 + $i)) + @('--perf-idle-seconds', "$IdleSeconds"))) 'doba-perf idle-seconds='
                })
            $ws = [double[]]@($rows | ForEach-Object { Get-Number $_ 'working-set-mb' })
            $pb = [double[]]@($rows | ForEach-Object { Get-Number $_ 'private-mb' })
            $mh = [double[]]@($rows | ForEach-Object { Get-Number $_ 'managed-heap-mb' })
            $gcc = [double[]]@($rows | ForEach-Object { Get-Number $_ 'gc-committed-mb' })
            $report.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-12} {5,-12}' -f $set, $rows.Count,
                ((F (Get-Median $ws) 'F1') + ' / ' + (F (Get-Worst $ws) 'F1')),
                ((F (Get-Median $pb) 'F1') + ' / ' + (F (Get-Worst $pb) 'F1')),
                (F (Get-Median $mh) 'F1'), (F (Get-Median $gcc) 'F1')))
        }
        $report.Add('')
    }

    # ---- Week and overlay scenario ---------------------------------------------------------------------------
    if (-not $SkipScenario) {
        $scenarioSets = @('demo', 'db-10k', 'db-dense-compact')
        Write-Step "Week scenario: $ScenarioRuns runs x $ScenarioRounds rounds for $($scenarioSets -join ', ')"
        $weekRows = New-Object System.Collections.Generic.List[string]
        $overlayRows = New-Object System.Collections.Generic.List[string]
        foreach ($set in $scenarioSets) {
            for ($i = 0; $i -lt $ScenarioRuns; $i++) {
                $lines = Invoke-Doba (@(Get-LaunchArgs $set (200 + $i)) + @('--perf-scenario', 'week', '--perf-rounds', "$ScenarioRounds"))
                $f = Get-Fields $lines 'doba-perf scenario=week'
                $weekRows.Add(('{0,-17} {1,-9} {2,-11} {3,-20} {4,-17} {5,-18} {6,-16} {7,-18} {8,-28} {9,-8}' -f $set,
                    ((V $f 'cards') + '+' + (V $f 'bars')), (V $f 'density'),
                    ((V $f 'render-p50-ms') + ' / ' + (V $f 'render-p95-ms') + ' / ' + (V $f 'render-max-ms')),
                    ((V $f 'render-over-16.7ms') + ' / ' + (V $f 'render-over-33.3ms') + ' / ' + (V $f 'render-over-50ms')),
                    ((V $f 'ui-p95-ms') + ' / ' + (V $f 'ui-max-ms') + ' / ' + (V $f 'ui-over-50ms')),
                    ((V $f 'render-long') + ' / ' + (V $f 'render-long-gc') + '  ' + (V $f 'ui-long') + ' / ' + (V $f 'ui-long-gc')),
                    ((V $f 'gc0') + '/' + (V $f 'gc1') + '/' + (V $f 'gc2') + ' (' + (V $f 'gc-pause-ms') + ')'),
                    (V $f 'warm-step-alloc-kb'), (V $f 'live-heap-mb')))
                $o = Get-Fields $lines 'doba-perf scenario=overlay'
                foreach ($mode in @('blur', 'plain')) {
                    $overlayRows.Add(('{0,-17} {1,-6} {2,-24} {3,-12} {4,-24} {5,-12}' -f $set, $mode,
                        ((V $o "$mode-open-render-p50-ms") + ' / ' + (V $o "$mode-open-render-p95-ms") + ' / ' + (V $o "$mode-open-render-max-ms")),
                        ((V $o "$mode-open-render-over-16.7ms") + ' / ' + (V $o "$mode-open-render-frames")),
                        ((V $o "$mode-close-render-p50-ms") + ' / ' + (V $o "$mode-close-render-p95-ms") + ' / ' + (V $o "$mode-close-render-max-ms")),
                        ((V $o "$mode-close-render-over-16.7ms") + ' / ' + (V $o "$mode-close-render-frames"))))
                }
            }
        }
        $report.Add("Week scenario ($ScenarioRounds rounds x 6 navigations; ms; render = render-thread frames, ui = UI-thread frames; a 60 fps frame is 16.7 ms)")
        $report.Add('  long = frames over 33.3 ms / of them with a GC inside; alloc = KB allocated per warm step: next, previous, Inbox, calendar, Month, Week')
        $report.Add(('{0,-17} {1,-9} {2,-11} {3,-20} {4,-17} {5,-18} {6,-16} {7,-18} {8,-28} {9,-8}' -f 'set', 'cards+bars', 'density', 'render p50/p95/max', 'render >16.7/33/50', 'ui p95/max/>50', 'long rend.  ui', 'GC all/g1/g2 (ms)', 'alloc KB/step', 'heap MB'))
        $weekRows | ForEach-Object { $report.Add($_) }
        $report.Add('')
        $report.Add('Overlay open/close over the Week view (New task dialog, 8 cycles per mode; render-thread frames inside the fades; ms)')
        $report.Add('  n/a = no render frames recorded: the window was covered or minimised during the run (keep it in front, unobstructed)')
        $report.Add(('{0,-17} {1,-6} {2,-24} {3,-12} {4,-24} {5,-12}' -f 'set', 'blur', 'open p50/p95/max', '>16.7/frames', 'close p50/p95/max', '>16.7/frames'))
        $overlayRows | ForEach-Object { $report.Add($_) }
        $report.Add('')
    }

    Save-Report $report
}
finally {
    Pop-Location
    # Nothing this script started may outlive it (Invoke-Doba already waits for or stops each process).
}
