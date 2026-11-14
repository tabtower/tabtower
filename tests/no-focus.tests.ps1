# Deck-half test for `session open --no-focus` and `session list --sleeping` (app 0.11.49).
#
# Same harness as close-tab.tests.ps1: a FAKE VSCode connector on TabTower's named pipe, so the
# whole deck path is driven without touching any of the user's real VSCode windows: the CLI verb,
# the refusals, the exact JSON pushed down the pipe, and the sleeping list. The extension half
# (wakeSessionQuietly) is not reachable from here; it needs a real window running connector 0.7.2.
#
# Needs the INSTALLED deck running, at 0.11.49 or later: an older deck has no --no-focus, and its
# `session open` would FOCUS, or LAUNCH VS Code on the fake folder, which is a window on the
# user's screen. The suite checks the version first and stops before anything else otherwise.
# Run it in the tool's own PowerShell process (see close-tab.tests.ps1 for why).

$ErrorActionPreference = 'Stop'
$exe  = "$env:LOCALAPPDATA\Programs\TabTower\TabTower.exe"
$ws   = Join-Path ([System.IO.Path]::GetTempPath()) 'tabtower-no-focus-fake-ws'
if (-not (Test-Path $ws)) { New-Item -ItemType Directory -Path $ws | Out-Null }

$pass = 0; $fail = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Host "  PASS  $name" }
    else     { $script:fail++; Write-Host "  FAIL  $name -- $detail" }
}

function Deck([string[]]$deckArgs) {
    $o = [System.IO.Path]::GetTempFileName()
    $e = [System.IO.Path]::GetTempFileName()
    $quoted = $deckArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process $exe -ArgumentList $quoted -Wait -PassThru `
                       -RedirectStandardOutput $o -RedirectStandardError $e -NoNewWindow
    $out = ((Get-Content $o -Raw -Encoding UTF8) ?? '') + ((Get-Content $e -Raw -Encoding UTF8) ?? '')
    Remove-Item $o, $e -Force
    [pscustomobject]@{ Code = $p.ExitCode; Out = ($out -replace "`r", '').Trim() }
}

if (-not ('NoFocusTest.User32' -as [type])) {
    Add-Type -Namespace NoFocusTest -Name User32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
'@
}
function Foreground { [NoFocusTest.User32]::GetForegroundWindow().ToInt64() }
# GetForegroundWindow answers 0 while the console session is disconnected or locked: then "it did
# not change" proves nothing, and the check says SKIP instead of passing.
$skip = 0
function Check-Foreground([long]$before, [string]$name = 'the foreground window did not change') {
    $after = Foreground
    if ($before -eq 0 -and $after -eq 0) { $script:skip++; Write-Host "  SKIP  $name -- no foreground window (desktop disconnected or locked)"; return }
    Check $name ($after -eq $before) "before=$before after=$after"
}
function CodeCount { @(Get-Process Code -ErrorAction SilentlyContinue).Count }

$ver = Deck @('status')
if ($ver.Out -notmatch 'TabTower (\d+)\.(\d+)\.(\d+)' -or
    ([version]"$($Matches[1]).$($Matches[2]).$($Matches[3])" -lt [version]'0.11.49')) {
    Write-Host "the installed deck is not 0.11.49 or later ($($ver.Out)); stopping before anything is sent"
    exit 2
}

$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'tabtower',
    [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
$pipe.Connect(5000)
$writer = New-Object System.IO.StreamWriter($pipe)
$writer.AutoFlush = $true
$script:buf = New-Object byte[] 16384
$script:read = $null

function Send-Sync([string]$version, [string[]]$labels) {
    $msg = @{
        Type = 'vscode-sync'; Workspace = $ws; Branch = 'fake'; Pid = $PID; Focused = $false
        Version = $version
        Tabs = @($labels | ForEach-Object { @{ Label = $_; Active = $false } })
    } | ConvertTo-Json -Depth 5 -Compress
    $writer.WriteLine($msg)
    Start-Sleep -Milliseconds 400
}

function Read-Pushed([int]$ms = 2000) {
    if ($null -eq $script:read) { $script:read = $pipe.ReadAsync($script:buf, 0, $script:buf.Length) }
    if (-not $script:read.Wait($ms)) { return '' }
    $text = [Text.Encoding]::UTF8.GetString($script:buf, 0, $script:read.Result)
    $script:read = $null
    return $text.Trim()
}

function First-Command([string]$pushed) {
    if (-not $pushed) { return $null }
    $pushed.Split("`n") | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.Cmd -eq 'openSession' } | Select-Object -First 1
}

$idA  = [guid]::NewGuid().ToString()   # closed, its tab is NOT in the window
$idB  = [guid]::NewGuid().ToString()   # closed, its tab IS in the window: sleeping
$idC  = [guid]::NewGuid().ToString()   # live, holds the one "NF Wake C" tab
$idC2 = [guid]::NewGuid().ToString()   # closed, also "NF Wake C": not sleeping, the tab is C's
$idD1 = [guid]::NewGuid().ToString()   # closed "NF Dup"
$idD2 = [guid]::NewGuid().ToString()   # closed "NF Dup": two sessions, one tab -> ambiguous
$all  = @($idA, $idB, $idC, $idC2, $idD1, $idD2)

try {
    Write-Host "Case 0: the flag validates its input"
    $r = Deck @('session', 'open', '--id', [guid]::NewGuid().ToString(), '--no-focus')
    Check 'an unknown session id is refused' ($r.Code -ne 0 -and $r.Out -match 'unknown session id') "[$($r.Code)] $($r.Out)"

    foreach ($pair in @(@($idA, 'NF Wake A'), @($idB, 'NF Wake B'), @($idC2, 'NF Wake C'), @($idD1, 'NF Dup'), @($idD2, 'NF Dup'))) {
        Deck @('session', 'start', '--id', $pair[0], '--workspace', $ws, '--title', $pair[1]) | Out-Null
        Deck @('session', 'end', '--id', $pair[0]) | Out-Null
    }

    Write-Host "Case 1: no connected window -> refused, nothing launched, focus untouched"
    $fg0 = Foreground; $code0 = CodeCount
    $r = Deck @('session', 'open', '--id', $idB, '--no-focus')
    Start-Sleep -Milliseconds 1500
    Check 'refused with a non-zero exit, and it says the window is not connected' ($r.Code -ne 0 -and $r.Out -match 'not connected' -and $r.Out -match 'nothing woken') "[$($r.Code)] $($r.Out)"
    Check-Foreground $fg0
    Check 'no VS Code process was started' ((CodeCount) -le $code0) "before=$code0 after=$(CodeCount)"
    Check 'nothing was pushed' ((Read-Pushed 800) -eq '')

    Write-Host "Case 2: connected (0.7.2), but the session's tab is not there -> refused"
    Send-Sync '0.7.2' @('NF Wake B', 'NF Wake C', 'NF Dup', 'Some other tab')
    Deck @('session', 'start', '--id', $idC, '--workspace', $ws, '--title', 'NF Wake C') | Out-Null
    Start-Sleep -Milliseconds 400
    $fg0 = Foreground
    $r = Deck @('session', 'open', '--id', $idA, '--no-focus')
    Check 'refused with a non-zero exit, and it says no tab' ($r.Code -ne 0 -and $r.Out -match 'no tab of it' -and $r.Out -match 'nothing woken') "[$($r.Code)] $($r.Out)"
    Check 'nothing was pushed for a tabless session' ((First-Command (Read-Pushed 1000)) -eq $null)
    Check-Foreground $fg0

    Write-Host "Case 3: connected (0.7.2), its tab is there -> woken quietly"
    $fg0 = Foreground; $code0 = CodeCount
    $r = Deck @('session', 'open', '--id', $idB, '--no-focus')
    Check 'accepted' ($r.Code -eq 0 -and $r.Out -match 'waking session' -and $r.Out -match 'without focus' -and $r.Out -notmatch 'predates') "[$($r.Code)] $($r.Out)"
    $cmd = First-Command (Read-Pushed 2500)
    Check 'an openSession command arrived' ($null -ne $cmd)
    if ($cmd) {
        Check 'SessionId is the one asked for' ($cmd.SessionId -eq $idB) $cmd.SessionId
        Check 'NoFocus is set' ($cmd.NoFocus -eq $true) "$($cmd.NoFocus)"
        Check 'Maximize is off (no side-bar collapse)' ($cmd.Maximize -eq $false) "$($cmd.Maximize)"
        Check 'not the terminal route' ($cmd.Terminal -eq $false) "$($cmd.Terminal)"
    }
    Start-Sleep -Milliseconds 800
    Check-Foreground $fg0
    Check 'no VS Code process was started' ((CodeCount) -le $code0) "before=$code0 after=$(CodeCount)"

    Write-Host "Case 4: session list --sleeping"
    $r = Deck @('session', 'list', '--sleeping')
    $lines = @($r.Out -split "`n")
    $lineB = $lines | Where-Object { $_ -match [regex]::Escape($idB) }
    Check 'exit 0' ($r.Code -eq 0) "[$($r.Code)] $($r.Out)"
    Check 'the sleeping session is listed, with its tab label' ($lineB -match 'sleeping' -and $lineB -match 'tab:"NF Wake B"' -and $lineB -notmatch 'ambiguous') "$lineB"
    Check 'a closed session with no tab is not listed' (-not ($r.Out -match [regex]::Escape($idA)))
    Check 'a live session is not listed' (-not ($r.Out -match [regex]::Escape($idC)))
    Check 'a closed session whose label the LIVE session holds is not listed' (-not ($r.Out -match [regex]::Escape($idC2)))
    $d1 = $lines | Where-Object { $_ -match [regex]::Escape($idD1) }
    $d2 = $lines | Where-Object { $_ -match [regex]::Escape($idD2) }
    Check 'two closed sessions on one tab are both listed, flagged ambiguous' ($d1 -match 'ambiguous' -and $d2 -match 'ambiguous') "d1=[$d1] d2=[$d2]"
    $r = Deck @('session', 'list')
    Check 'the plain list still leaves the sleeping session out' (-not ($r.Out -match [regex]::Escape($idB)))

    Write-Host "Case 5: a 0.7.1 window is still woken, and the answer says the hand-back is missing"
    Send-Sync '0.7.1' @('NF Wake B', 'NF Wake C', 'NF Dup', 'Some other tab')
    [void](Read-Pushed 500)
    $fg0 = Foreground
    $r = Deck @('session', 'open', '--id', $idB, '--no-focus')
    Check 'accepted, naming the old extension' ($r.Code -eq 0 -and $r.Out -match 'predates 0\.7\.2' -and $r.Out -match '0\.7\.1') "[$($r.Code)] $($r.Out)"
    $cmd = First-Command (Read-Pushed 2500)
    Check 'openSession with NoFocus and no Maximize was sent' ($null -ne $cmd -and $cmd.NoFocus -eq $true -and $cmd.Maximize -eq $false) "$($cmd | ConvertTo-Json -Compress)"
    Check-Foreground $fg0

    Write-Host "Case 6: the tab is gone from the window -> refused again"
    Send-Sync '0.7.2' @('NF Wake C', 'Some other tab')
    [void](Read-Pushed 500)
    $r = Deck @('session', 'open', '--id', $idB, '--no-focus')
    Check 'refused once its tab is no longer reported' ($r.Code -ne 0 -and ($r.Out -match 'no tab of it' -or $r.Out -match 'closed its tab')) "[$($r.Code)] $($r.Out)"
    Check 'nothing was pushed' ((First-Command (Read-Pushed 1000)) -eq $null)
}
finally {
    foreach ($id in $all) { Deck @('session', 'end', '--id', $id) | Out-Null }
    $writer.Dispose(); $pipe.Dispose()
    Start-Sleep -Milliseconds 900
    Deck @('remove', '--match', 'no-focus-fake-ws') | Out-Null
}

Write-Host ""
Write-Host "==== $pass passed, $fail failed, $skip skipped ===="
if ($fail -gt 0) { exit 1 }
