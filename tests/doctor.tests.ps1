# Tests for `tabtower doctor`, the setup check also shown once on the very first start.
# Runs the built exe against temp settings files and a temp extensions folder - no app
# instance needed.
# Usage: powershell -NoProfile -File tests\doctor.tests.ps1 [-Exe <path>]
[CmdletBinding()]
param(
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not available in param defaults under PowerShell 5.1 - resolve here.
if (-not $Exe) { $Exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'bin\Debug\net10.0-windows\TabTower.exe' }
if (-not (Test-Path $Exe)) { throw "exe not found: $Exe - run 'dotnet build' first" }

$workDir = Join-Path $env:TEMP ("tabtower-doctor-tests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDir | Out-Null
$script:failed = 0
$script:passed = 0

function Assert($condition, [string]$name) {
    if ($condition) { $script:passed++; Write-Host "  PASS  $name" }
    else            { $script:failed++; Write-Host "  FAIL  $name" -ForegroundColor Red }
}

# TabTower.exe is a GUI-subsystem binary: Start-Process -Wait with redirected streams is the
# only way to wait for it and read what it printed.
function Invoke-Exe([string]$argStr) {
    $outFile = Join-Path $workDir 'stdout.txt'
    $errFile = Join-Path $workDir 'stderr.txt'
    $p = Start-Process -FilePath $Exe -ArgumentList $argStr -Wait -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    $text = ''
    foreach ($f in $outFile, $errFile) {
        if ((Get-Item $f).Length -gt 0) { $text += (Get-Content $f -Raw -Encoding UTF8) }
    }
    return @{ ExitCode = $p.ExitCode; Output = $text }
}

function Invoke-Doctor([string]$settings, [string]$extensions) {
    Invoke-Exe ('doctor --settings "' + $settings + '" --extensions-dir "' + $extensions + '"')
}

# An extensions folder with both extensions in it, and one with neither.
$extFull = Join-Path $workDir 'ext-full'
New-Item -ItemType Directory -Path (Join-Path $extFull 'tabtower.tabtower-connector-0.7.0') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $extFull 'anthropic.claude-code-2.1.0-win32-x64') -Force | Out-Null
$extEmpty = Join-Path $workDir 'ext-empty'
New-Item -ItemType Directory -Path $extEmpty | Out-Null

# --- Case 1: everything in place -> exit 0, three ok lines ---
Write-Host "Case 1: hooks installed, both extensions present"
$s = Join-Path $workDir 'full.json'
Invoke-Exe ('install-hooks --settings "' + $s + '"') | Out-Null
$r = Invoke-Doctor $s $extFull
Assert ($r.ExitCode -eq 0) "exit code 0"
Assert ($r.Output -match 'ok\s+hooks: 11 of 11') "hooks ok ($($r.Output.Trim() -replace '\s+', ' '))"
Assert ($r.Output -match 'ok\s+TabTower connector extension: tabtower\.tabtower-connector-0\.7\.0') "connector ok"
Assert ($r.Output -match 'ok\s+Claude Code extension: anthropic\.claude-code-') "Claude Code extension ok"
Assert ($r.Output -notmatch 'FIX') "no FIX line"

# --- Case 2: no settings file -> FIX, exit 1 ---
Write-Host "Case 2: settings file missing"
$r = Invoke-Doctor (Join-Path $workDir 'absent.json') $extFull
Assert ($r.ExitCode -eq 1) "exit code 1"
Assert ($r.Output -match 'FIX\s+hooks: .+does not exist.+tabtower install-hooks') "hooks FIX names the fix"

# --- Case 3: empty file and bare {} -> none registered ---
Write-Host "Case 3: empty file and bare {}"
$s = Join-Path $workDir 'empty.json'
Set-Content -Path $s -Value '' -NoNewline
$r = Invoke-Doctor $s $extFull
Assert ($r.ExitCode -eq 1 -and $r.Output -match 'FIX\s+hooks: none registered') "empty file: none registered"
$s = Join-Path $workDir 'braces.json'
Set-Content -Path $s -Value '{}'
$r = Invoke-Doctor $s $extFull
Assert ($r.ExitCode -eq 1 -and $r.Output -match 'FIX\s+hooks: none registered') "{}: none registered"

# --- Case 4: one event missing -> "10 of 11" ---
Write-Host "Case 4: one hook event removed"
$s = Join-Path $workDir 'partial.json'
Invoke-Exe ('install-hooks --settings "' + $s + '"') | Out-Null
$json = Get-Content $s -Raw | ConvertFrom-Json
$json.hooks.PSObject.Properties.Remove('StopFailure')
$json | ConvertTo-Json -Depth 20 | Set-Content -Path $s
$r = Invoke-Doctor $s $extFull
Assert ($r.ExitCode -eq 1) "exit code 1"
Assert ($r.Output -match 'FIX\s+hooks: 10 of 11 registered') "reports 10 of 11"

# --- Case 5: malformed JSON -> FIX without touching the file ---
Write-Host "Case 5: malformed JSON"
$s = Join-Path $workDir 'broken.json'
Set-Content -Path $s -Value '{ "hooks": '
$before = Get-Content $s -Raw
$r = Invoke-Doctor $s $extFull
Assert ($r.ExitCode -eq 1 -and $r.Output -match 'not valid JSON') "reports invalid JSON"
Assert ((Get-Content $s -Raw) -eq $before) "file untouched"

# --- Case 6: the hooks run a script that is gone ---
Write-Host "Case 6: hook script path does not exist"
$s = Join-Path $workDir 'gone.json'
Invoke-Exe ('install-hooks --settings "' + $s + '"') | Out-Null
$json = Get-Content $s -Raw | ConvertFrom-Json
foreach ($evt in $json.hooks.PSObject.Properties) {
    foreach ($h in $evt.Value[0].hooks) {
        $h.command = $h.command -replace '-File "[^"]+"', '-File "C:\nowhere\tabtower-hook.ps1"'
    }
}
$json | ConvertTo-Json -Depth 20 | Set-Content -Path $s
$r = Invoke-Doctor $s $extFull
Assert ($r.ExitCode -eq 1 -and $r.Output -match 'FIX\s+hooks: they run C:\\nowhere\\tabtower-hook\.ps1, which does not exist') "names the missing script ($($r.Output.Trim() -replace '\s+', ' '))"

# --- Case 7: registrations of the former name ---
Write-Host "Case 7: hooks still run the former script"
$formerScript = 'sessiondeck-hook.ps1'   # public-gate: allow
$s = Join-Path $workDir 'former.json'
@'
{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "powershell -File \"C:\\old\\FORMER_SCRIPT\" Stop" } ] } ] } }
'@.Replace('FORMER_SCRIPT', $formerScript) | Set-Content -Path $s
$r = Invoke-Doctor $s $extFull
Assert ($r.ExitCode -eq 1 -and $r.Output -match "FIX\s+hooks: some still run the script of the app's former name") "reports the former name"

# --- Case 8: extensions missing, and no extensions folder at all ---
Write-Host "Case 8: extensions"
$s = Join-Path $workDir 'full.json'
$r = Invoke-Doctor $s $extEmpty
Assert ($r.ExitCode -eq 1) "empty folder: exit code 1"
Assert ($r.Output -match 'FIX\s+TabTower connector extension: not installed') "empty folder: connector FIX"
Assert ($r.Output -match 'FIX\s+Claude Code extension: not installed') "empty folder: Claude Code FIX"
$r = Invoke-Doctor $s (Join-Path $workDir 'no-such-folder')
Assert ($r.ExitCode -eq 0) "no folder: exit code 0 (cannot be checked, not a problem)"
Assert ($r.Output -match 'skip\s+VS Code extensions: no folder') "no folder: skip line"

# --- Case 9: an unknown option is refused ---
Write-Host "Case 9: unknown option"
$r = Invoke-Exe 'doctor --bogus'
Assert ($r.ExitCode -eq 1 -and $r.Output -match "unknown option '--bogus'") "refused with usage"

Write-Host ""
Write-Host "==== $script:passed passed, $script:failed failed ===="
Remove-Item -Path $workDir -Recurse -Force -ErrorAction SilentlyContinue
if ($script:failed -gt 0) { exit 1 }
exit 0
