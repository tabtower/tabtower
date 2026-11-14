<#
.SYNOPSIS
  Tests for rebuilding cards at startup from Claude Code's live-session registry, without starting
  TabTower itself.

.DESCRIPTION
  1. Builds tests/recovery/RecoveryHarness, which compiles the app's own Services/RegistryRecovery.cs
     and Services/SessionRegistry.cs, and runs its in-process self-test of the rules.
  2. Builds a fixture registry in a temp folder against REAL processes and runs the harness's
     `plan` over it, the same read and the same plan the deck makes at startup:
       - a live session: a hidden process running from a folder named like the VS Code extension's,
         with its true start time and a transcript on disk          -> card created
       - a dead session: a process that has already exited          -> skipped
       - a recycled pid: the live process's pid, a wrong start time  -> skipped
       - a headless run: a live process outside the extension folder -> skipped
       - a live session with no transcript yet                       -> skipped
       - a card closed as orphaned whose process is live             -> revived
       - a card closed as replaced whose process is live             -> left closed
       - a terminal session (entrypoint cli) and a Claude Desktop one (claude-desktop), live
         processes outside the extension folder, with transcripts     -> card created (0.11.68)
       - a `claude --bg` run (entrypoint cli, kind bg)                -> skipped
     Then `probe` over the same registry: a session whose process is running reads Live, which
     keeps a card click from resuming it in a terminal beside that process (0.11.62).
     It never touches a running TabTower, its pipe, its config or the real registry. The processes
     it starts are copies of ping.exe, hidden, and it stops them itself.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\session-recovery.tests.ps1
#>
$ErrorActionPreference = 'Continue'
$proj = Join-Path $PSScriptRoot 'recovery\RecoveryHarness\RecoveryHarness.csproj'
$exe = Join-Path $PSScriptRoot 'recovery\RecoveryHarness\bin\Debug\net10.0\RecoveryHarness.exe'
$failed = 0

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "PASS  $what" } else { Write-Host "FAIL  $what"; $script:failed++ }
}

dotnet build $proj -c Debug -v:q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL  harness build'; exit 1 }

& $exe selftest
Check ($LASTEXITCODE -eq 0) 'in-process self-test'

$root = Join-Path ([IO.Path]::GetTempPath()) "tabtower-recovery-$PID"
$sessions = Join-Path $root 'sessions'
$projects = Join-Path $root 'projects'
$ideDir = Join-Path $root 'extensions\anthropic.claude-code-0.0.0-fixture\native-binary'
$cwd = 'C:\fixture\repo'
$slug = 'C--fixture-repo'
New-Item -ItemType Directory -Force $sessions, (Join-Path $projects $slug), $ideDir | Out-Null
Copy-Item "$env:SystemRoot\System32\PING.EXE" (Join-Path $ideDir 'claude.exe')

$ping = @('-n', '120', '127.0.0.1')
$ide = Start-Process -FilePath (Join-Path $ideDir 'claude.exe') -ArgumentList $ping -WindowStyle Hidden -PassThru
$cli = Start-Process -FilePath "$env:SystemRoot\System32\PING.EXE" -ArgumentList $ping -WindowStyle Hidden -PassThru
$gone = Start-Process -FilePath "$env:SystemRoot\System32\PING.EXE" -ArgumentList @('-n', '1', '127.0.0.1') -WindowStyle Hidden -PassThru
$goneStart = $gone.StartTime.ToFileTimeUtc().ToString()
$gone.WaitForExit()
try {
    function Start-Of($p) { (Get-Process -Id $p.Id).StartTime.ToFileTimeUtc().ToString() }
    $ids = @{
        live      = '11111111-1111-4111-8111-111111111111'
        dead      = '22222222-2222-4222-8222-222222222222'
        recycled  = '33333333-3333-4333-8333-333333333333'
        headless  = '44444444-4444-4444-8444-444444444444'
        notyet    = '55555555-5555-4555-8555-555555555555'
        orphaned  = '66666666-6666-4666-8666-666666666666'
        replaced  = '77777777-7777-4777-8777-777777777777'
        terminal  = '99999999-9999-4999-8999-999999999991'
        desktop   = '99999999-9999-4999-8999-999999999992'
        bgrun     = '99999999-9999-4999-8999-999999999993'
    }
    $records = @(
        @{ file = '1'; id = $ids.live;     pid = $ide.Id;  start = (Start-Of $ide) }
        @{ file = '2'; id = $ids.dead;     pid = $gone.Id; start = $goneStart }
        @{ file = '3'; id = $ids.recycled; pid = $ide.Id;  start = '1' }
        @{ file = '4'; id = $ids.headless; pid = $cli.Id;  start = (Start-Of $cli) }
        @{ file = '5'; id = $ids.notyet;   pid = $ide.Id;  start = (Start-Of $ide) }
        @{ file = '6'; id = $ids.orphaned; pid = $ide.Id;  start = (Start-Of $ide) }
        @{ file = '7'; id = $ids.replaced; pid = $ide.Id;  start = (Start-Of $ide) }
        @{ file = '8'; id = $ids.terminal; pid = $cli.Id;  start = (Start-Of $cli); entrypoint = 'cli' }
        @{ file = '9'; id = $ids.desktop;  pid = $cli.Id;  start = (Start-Of $cli); entrypoint = 'claude-desktop' }
        @{ file = '10'; id = $ids.bgrun;   pid = $cli.Id;  start = (Start-Of $cli); entrypoint = 'cli'; kind = 'bg' }
    )
    foreach ($r in $records) {
        $json = [ordered]@{ pid = $r.pid; sessionId = $r.id; cwd = $cwd; procStart = $r.start
                            kind = $(if ($r.kind) { $r.kind } else { 'interactive' })
                            entrypoint = $(if ($r.entrypoint) { $r.entrypoint } else { 'claude-vscode' }) } | ConvertTo-Json
        [IO.File]::WriteAllText((Join-Path $sessions "$($r.file).json"), $json)
        if ($r.id -ne $ids.notyet) { [IO.File]::WriteAllText((Join-Path $projects "$slug\$($r.id).jsonl"), '{}') }
    }
    [IO.File]::WriteAllText((Join-Path $sessions 'broken.json'), '{ not json')

    $out = & $exe plan --sessions-dir $sessions --projects-dir $projects `
        --known "$($ids.orphaned)=closed:orphaned" --known "$($ids.replaced)=closed:replaced"
    $out | ForEach-Object { Write-Host "      $_" }
    function Line([string]$id) { $out | Where-Object { $_ -match "session=$id " } }
    Check ((Line $ids.live) -match '^CREATE') 'live session with a proven identity: card created'
    Check ((Line $ids.dead) -match '^SKIP .*not running') 'exited process: no card'
    Check ((Line $ids.recycled) -match '^SKIP .*not running') 'pid with a different start time (recycled): no card'
    Check ((Line $ids.headless) -match '^SKIP .*binary') "live process outside the extension's folder: no card"
    Check ((Line $ids.notyet) -match '^SKIP .*transcript') 'live session with no transcript yet: no card'
    Check ((Line $ids.orphaned) -match '^REVIVE') 'card closed as orphaned, process live: revived'
    Check ((Line $ids.replaced) -match '^SKIP') 'card closed as replaced: left closed'
    Check ((Line $ids.terminal) -match '^CREATE') 'live terminal session outside the extension folder: card created (0.11.68)'
    Check ((Line $ids.desktop) -match '^CREATE') 'live Claude Desktop session: card created (0.11.68)'
    Check ((Line $ids.bgrun) -match '^SKIP .*kind=bg') '`claude --bg` run: no card'
    $cards = @($out | Select-Object -Skip (([array]::IndexOf($out, 'CARDS:')) + 1) | ForEach-Object { $_.Trim() })
    Check (($cards -join ',') -eq "$($ids.live),$($ids.orphaned),$($ids.terminal),$($ids.desktop)") 'resulting card list is exactly the live, the revived, the terminal and the Desktop session'
    # D1 (0.11.68): the orphan sweep keeps a terminal or Desktop card while SessionRegistry.Probe reads
    # Live for it; the probe below is that read, over the same fixture.

    # 0.11.62: a card click resumes a session in a terminal only when the registry does not show its
    # process running (MainWindow.OpenSessionInVscode over SessionRegistry.Probe). The live record is
    # the 03-11-2026 shape: a running process from the extension's folder with its true start time,
    # where the click opened a second `claude --resume` beside it.
    $savedConfigDir = $env:CLAUDE_CONFIG_DIR
    $env:CLAUDE_CONFIG_DIR = $root
    try {
        function Probe([string]$id) { (& $exe probe $id) -join ' ' }
        Check ((Probe $ids.live) -match '^Live$') 'probe: its own process is running, so no terminal resume'
        Check ((Probe $ids.headless) -match '^Live$') 'probe: a running CLI process counts the same'
        Check ((Probe $ids.terminal) -match '^Live$') 'probe: a terminal session whose process runs reads Live, so the orphan sweep keeps its card'
        Check ((Probe $ids.desktop) -match '^Live$') 'probe: a Desktop session whose process runs reads Live, so the orphan sweep keeps its card'
        Check ((Probe $ids.dead) -match '^Gone') 'probe: its process has exited, so the terminal resume goes'
        Check ((Probe $ids.recycled) -match '^Gone') 'probe: its pid now runs with another start time, so the terminal resume goes'
        Check ((Probe '88888888-8888-4888-8888-888888888888') -match '^Gone') 'probe: no process registered for it, so the terminal resume goes'
    }
    finally { $env:CLAUDE_CONFIG_DIR = $savedConfigDir }
}
finally {
    foreach ($p in @($ide, $cli)) { if (-not $p.HasExited) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue } }
    Start-Sleep -Milliseconds 300
    Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
}

if ($failed -gt 0) { Write-Host "$failed FAILED"; exit 1 }
Write-Host 'ALL PASSED'
exit 0
