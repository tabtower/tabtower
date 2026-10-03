# TabTower installer.
# Run from the extracted release zip. No admin rights required - everything is per-user.
# Upgrading = re-running this same script over a newer zip; every step is idempotent.
# PowerShell 5.1 compatible.
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\TabTower"
)

$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot

# TabTower.exe is a GUI-subsystem binary: PowerShell 5.1 neither waits for it nor
# captures its output via `&`. Start-Process -Wait with redirected streams does both.
function Invoke-TabTower([string]$ExePath, [string]$Arguments) {
    $outFile = [IO.Path]::GetTempFileName()
    $errFile = [IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath $ExePath -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $outFile -RedirectStandardError $errFile
        $text = @()
        foreach ($f in $outFile, $errFile) {
            if ((Get-Item $f).Length -gt 0) { $text += (Get-Content $f -Encoding UTF8) }
        }
        return @{ ExitCode = $p.ExitCode; Output = $text }
    } finally {
        Remove-Item $outFile, $errFile -ErrorAction SilentlyContinue
    }
}

# True when the destination already holds byte-identical content. Length first because it
# settles almost every mismatch for free; the hash is what makes the check trustworthy, since
# `dotnet publish` re-stamps the write time of every runtime assembly on every run while the
# bytes stay identical - comparing timestamps would rewrite the entire ~150MB payload each time.
function Test-SameContent([string]$Source, [string]$Destination) {
    if (-not (Test-Path $Destination)) { return $false }
    if ((Get-Item $Source).Length -ne (Get-Item $Destination).Length) { return $false }
    return (Get-Sha256 $Source) -eq (Get-Sha256 $Destination)
}

# .NET directly rather than Get-FileHash: Windows PowerShell started from a PowerShell 7 shell
# inherits PSModulePath from it, cannot load Get-FileHash, and the install then stops halfway
# with the running app already quit.
function Get-Sha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try { return [System.BitConverter]::ToString($sha.ComputeHash($stream)) }
    finally { $stream.Dispose(); $sha.Dispose() }
}

if (-not (Test-Path (Join-Path $src 'TabTower.exe'))) {
    Write-Error "TabTower.exe not found next to install.ps1 - run this script from the extracted release zip."
}

# --- 1. Stop a running instance (clean quit first: a forced kill skips the AppBar
#        release and leaves the Windows work area shrunken) ---
if (Get-Process TabTower -ErrorAction SilentlyContinue) {
    Write-Host "Stopping the running TabTower instance..."
    Invoke-TabTower (Join-Path $src 'TabTower.exe') 'quit' | Out-Null
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline -and (Get-Process TabTower -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 250
    }
    if (Get-Process TabTower -ErrorAction SilentlyContinue) {
        Write-Warning "TabTower did not exit within 5s - forcing (the reserved zone may need the app restarted to reset)."
        Get-Process TabTower -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
}

# --- 1b. Retire an install of the app's former name. Its settings are copied over by the app
#         itself on first start and its hooks are replaced by install-hooks below; what is left
#         for this script is the running process, its PATH entry and its VSCode extension. Its
#         folder is left in place and named in the summary: deleting files this run did not
#         write is not an installer's call. ---
$legacyName = 'SessionDeck'                                    # public-gate: allow
$legacyExtension = 'eyal-sinay.sessiondeck-connector'          # public-gate: allow
$legacyDir = Join-Path $env:LOCALAPPDATA "Programs\$legacyName"
$legacyExe = Join-Path $legacyDir "$legacyName.exe"
if (Get-Process $legacyName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping the running $legacyName instance (the app's former name)..."
    if (Test-Path $legacyExe) { Invoke-TabTower $legacyExe 'quit' | Out-Null }
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline -and (Get-Process $legacyName -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 250
    }
    Get-Process $legacyName -ErrorAction SilentlyContinue | Stop-Process -Force
}
$legacyPathStatus = $null
$userPathNow = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($userPathNow) {
    $entries = $userPathNow -split ';'
    $kept = $entries | Where-Object { $_ -and ($_.TrimEnd('\') -ine $legacyDir.TrimEnd('\')) }
    if (@($kept).Count -ne @($entries | Where-Object { $_ }).Count) {
        [Environment]::SetEnvironmentVariable('Path', ($kept -join ';'), 'User')
        $legacyPathStatus = "removed $legacyDir from the user PATH"
    }
}

# --- 2. Copy the zip content to the install dir ---
$resolvedSrc = (Resolve-Path $src).Path.TrimEnd('\')
if (Test-Path $InstallDir) {
    $resolvedDst = (Resolve-Path $InstallDir).Path.TrimEnd('\')
} else {
    $resolvedDst = $InstallDir.TrimEnd('\')
}
if ($resolvedSrc -ieq $resolvedDst) {
    Write-Host "Running from the install directory itself - skipping the copy step."
} else {
    # Write only what actually changed. An upgrade normally touches TabTower.dll and little
    # else; rewriting all ~200 files unconditionally is what used to stall the machine for about
    # a minute per install - explorer.exe at 200,000+ soft page faults per second, measured on
    # eight installs. Files the source no longer carries are left in place on purpose: the .NET host
    # loads by deps.json, so a leftover assembly is inert, and deleting by pattern here would be
    # the one step in this script capable of destroying something it did not put there.
    Write-Host "Installing to $InstallDir ..."
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    $written = 0
    $skipped = 0
    $writtenBytes = 0
    foreach ($file in Get-ChildItem -Path $resolvedSrc -Recurse -File) {
        $relative = $file.FullName.Substring($resolvedSrc.Length + 1)
        $target = Join-Path $resolvedDst $relative
        if (Test-SameContent $file.FullName $target) { $skipped++; continue }
        $targetDir = Split-Path $target -Parent
        if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Force -Path $targetDir | Out-Null }
        # Retried, because "no TabTower is running" is not a state this machine reaches.
        # Every Claude Code hook invokes TabTower.exe as a CLI client, so on a busy night a
        # transient client process is almost always alive, and it loads the WPF assemblies just
        # like the app does. On 11-09-2026 one such client grabbed WindowsBase.dll mid-copy: the
        # script died there and left the install dir half on the new publish and half on the old,
        # with the app shut down. A client lives a second or two, so a few spaced retries clear
        # it; a genuinely held file still fails, loudly, at the end rather than mid-way.
        $copied = $false
        for ($try = 1; $try -le 8 -and -not $copied; $try++) {
            try { Copy-Item -Path $file.FullName -Destination $target -Force -ErrorAction Stop; $copied = $true }
            catch { if ($try -eq 8) { throw } ; Start-Sleep -Milliseconds 400 }
        }
        $written++
        $writtenBytes += $file.Length
    }
    Write-Host ("  wrote {0} file(s), {1:N1} MB; {2} already up to date." -f $written, ($writtenBytes / 1MB), $skipped)
}
$exe = Join-Path $InstallDir 'TabTower.exe'

# --- 3. Add the install dir to the user PATH (no-op if already there) ---
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (-not $userPath) { $userPath = '' }
$onPath = ($userPath -split ';') | Where-Object { $_ -and ($_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\')) }
if ($onPath) {
    $pathStatus = 'already on the user PATH'
} else {
    $newPath = if ($userPath) { $userPath.TrimEnd(';') + ';' + $InstallDir } else { $InstallDir }
    [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    $pathStatus = 'added to the user PATH (open a new terminal to pick it up)'
}
if (-not (($env:Path -split ';') | Where-Object { $_ -and ($_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\')) })) {
    $env:Path += ';' + $InstallDir
}

# --- 4. Install the VSCode extension (warning, not failure: the app works without it;
#        only tab activation and tab labels are lost) ---
$extVersion = $null
$vsix = Get-ChildItem -Path $src -Filter 'tabtower-connector-*.vsix' -ErrorAction SilentlyContinue |
    Sort-Object { [version]($_.BaseName -replace '^tabtower-connector-', '') } |
    Select-Object -Last 1
$codeCmd = Get-Command code -ErrorAction SilentlyContinue
if (-not $vsix) {
    Write-Warning "No tabtower-connector-*.vsix found in the zip - skipping the VSCode extension."
} elseif (-not $codeCmd) {
    Write-Warning "VSCode 'code' command not found on PATH - skipping the extension. Install it later with: code --install-extension `"$($vsix.FullName)`""
} else {
    Write-Host "Installing the VSCode extension ($($vsix.Name))..."
    # `code` writes Node's deprecation warnings to stderr. Under $ErrorActionPreference
    # 'Stop' PowerShell 5.1 turns a redirected stderr line into a terminating
    # NativeCommandError, so the installer died here - after copying the files, but
    # before registering the hooks and starting the app (09-08-2026, Node DEP0169).
    # The extension is explicitly a warning-not-failure step; keep it that way.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        # The former-name extension speaks to a pipe this app no longer serves; left installed
        # it would only retry forever in every VSCode window. No-op when it is not there.
        & code --uninstall-extension $legacyExtension 2>&1 | Out-Null
        & code --install-extension $vsix.FullName --force 2>&1 | Out-Null
        $listed = & code --list-extensions --show-versions 2>&1 |
            Where-Object { $_ -match 'tabtower-connector@(.+)$' } | Select-Object -First 1
    } finally {
        $ErrorActionPreference = $prevEap
    }
    if ($listed -match 'tabtower-connector@(.+)$') { $extVersion = $Matches[1] }
    if (-not $extVersion) { Write-Warning "Extension install did not verify - check 'code --list-extensions --show-versions'." }
}

# --- 5. Register the Claude Code hooks (writes the real install path into settings.json) ---
Write-Host "Registering the Claude Code hooks..."
$hooksResult = Invoke-TabTower $exe 'install-hooks'
$hooksOutput = $hooksResult.Output
$hooksOutput | ForEach-Object { Write-Host "  $_" }
if ($hooksResult.ExitCode -ne 0) {
    Write-Error "install-hooks failed - see the message above. Nothing was written to settings.json."
}
$backupLine = $hooksOutput | Where-Object { $_ -match 'backup:\s*(.+)$' } | Select-Object -First 1
$backupPath = if ($backupLine -match 'backup:\s*(.+)$') { $Matches[1].Trim() } else { '(none - settings.json did not exist)' }

# --- 6. Start the app ---
Write-Host "Starting TabTower..."
Start-Process -FilePath $exe

# --- 7. Summary: the three parts update independently and can drift apart -
#        printing all three versions makes a mismatch visible ---
$appVersion = ((Get-Item $exe).VersionInfo.ProductVersion -split '\+')[0]
$hookScript = Join-Path $InstallDir 'hooks\tabtower-hook.ps1'
$hooksVersion = $null
$verMatch = Select-String -Path $hookScript -Pattern '^#\s*Version:\s*([0-9][0-9.]*)' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($verMatch) { $hooksVersion = $verMatch.Matches[0].Groups[1].Value }

Write-Host ""
Write-Host "=== TabTower installed ==="
Write-Host "  Location:       $InstallDir"
Write-Host "  PATH:           $pathStatus"
Write-Host "  Hooks backup:   $backupPath"
Write-Host "  Versions:"
Write-Host "    app:          $appVersion"
Write-Host "    extension:    $(if ($extVersion) { $extVersion } else { 'not installed' })"
Write-Host "    hooks:        $(if ($hooksVersion) { $hooksVersion } else { 'unknown' })"
if ($legacyPathStatus -or (Test-Path $legacyDir)) {
    Write-Host "  Former name ($legacyName):"
    if ($legacyPathStatus) { Write-Host "    $legacyPathStatus" }
    if (Test-Path $legacyDir) {
        Write-Host "    its program folder is still at $legacyDir. TabTower copies the settings on its first start; once it shows your cards, the folder can be deleted:"
        Write-Host "      Remove-Item -Recurse -Force `"$legacyDir`""
    }
}
Write-Host ""
Write-Host "New Claude Code sessions will now appear as cards in TabTower."
