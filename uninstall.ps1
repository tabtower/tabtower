# TabTower uninstaller. Removes the app, the Claude Code hooks, the VSCode extension,
# the user-PATH entry and the auto-start registry value. User settings in
# %APPDATA%\TabTower are kept unless -PurgeConfig is passed.
# PowerShell 5.1 compatible.
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\TabTower",
    [switch]$PurgeConfig
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallDir 'TabTower.exe'

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

# One entry of the user PATH, added or removed in the registry value itself.
#
# Not [Environment]::GetEnvironmentVariable / SetEnvironmentVariable: the getter hands the value
# back with every %VARIABLE% already expanded and the setter writes a plain string. One round
# trip through them froze every %USERPROFILE%-style entry into a literal path and changed the
# value's type from REG_EXPAND_SZ to REG_SZ (measured on a first install of 0.11.17). Here the
# text is read unexpanded, only the one entry is touched, every other character is written back
# as it was found, and so is the type.
#
# Returns 'added', 'present', 'removed', 'absent' or 'unsupported' (the value exists but is not
# a text type, and is left alone). Only 'added' and 'removed' write anything.
# -SubKey exists for tests\user-path.tests.ps1, which aims it at a scratch key.
# Keep this function identical in install.ps1 and uninstall.ps1; that test compares the two.
function Edit-UserPath {
    param(
        [Parameter(Mandatory)][string]$Entry,
        [switch]$Remove,
        [string]$SubKey = 'Environment'
    )
    $hkcu = [Microsoft.Win32.Registry]::CurrentUser
    $key = $hkcu.OpenSubKey($SubKey, $true)
    if (-not $key) {
        if ($Remove) { return 'absent' }
        $key = $hkcu.CreateSubKey($SubKey)
    }
    try {
        # What Windows itself uses for a user Path; only used when there is no value yet.
        $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
        $raw = ''
        if ($key.GetValueNames() -contains 'Path') {
            $kind = $key.GetValueKind('Path')
            $textKinds = [Microsoft.Win32.RegistryValueKind]::String, [Microsoft.Win32.RegistryValueKind]::ExpandString
            if ($textKinds -notcontains $kind) { return 'unsupported' }
            $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        } elseif ($Remove) {
            return 'absent'
        }

        # An entry is ours when it names the same folder: case, a trailing backslash and
        # surrounding quotes aside, and after expansion where Windows would expand it too.
        $want = $Entry.Trim().Trim('"').TrimEnd('\')
        $kept = New-Object System.Collections.Generic.List[string]
        $found = $false
        foreach ($part in @(if ($raw.Length -gt 0) { $raw.Split(';') })) {
            $text = $part.Trim().Trim('"')
            if ($kind -eq [Microsoft.Win32.RegistryValueKind]::ExpandString) {
                $text = [Environment]::ExpandEnvironmentVariables($text)
            }
            if ($text.Length -gt 0 -and $text.TrimEnd('\') -ieq $want) { $found = $true } else { $kept.Add($part) }
        }

        if ($Remove) {
            if (-not $found) { return 'absent' }
            $key.SetValue('Path', ($kept -join ';'), $kind)
            return 'removed'
        }
        if ($found) { return 'present' }
        # A value that ends with a separator keeps ending with one, so that removing the entry
        # again gives back exactly the text that was there before.
        $new = if ($raw.Length -eq 0) { $Entry } elseif ($raw.EndsWith(';')) { $raw + $Entry + ';' } else { $raw + ';' + $Entry }
        $key.SetValue('Path', $new, $kind)
        return 'added'
    } finally {
        $key.Dispose()
    }
}

# Tells running programs (Explorer first of all) that the user environment changed, so a
# terminal opened afterwards has the new PATH without a sign-out. .NET's SetEnvironmentVariable
# sent this as a side effect; a registry write does not. Failing to send it is not an error:
# the PATH is already written and takes effect at the next sign-in.
# Keep this function identical in install.ps1 and uninstall.ps1 as well.
function Send-EnvironmentChanged {
    param([IntPtr]$Window = [IntPtr]0xffff)   # HWND_BROADCAST: every top-level window
    try {
        if (-not ('TabTowerSetup.User32' -as [type])) {
            Add-Type -Namespace TabTowerSetup -Name User32 -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
'@
        }
        $result = [UIntPtr]::Zero
        # 0x1A = WM_SETTINGCHANGE, 2 = SMTO_ABORTIFHUNG, at most one second per window.
        [void][TabTowerSetup.User32]::SendMessageTimeout($Window, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 1000, [ref]$result)
    } catch {
        Write-Warning "Could not tell running programs about the PATH change ($($_.Exception.Message)). It takes effect at your next sign-in."
    }
}

# --- 1. Stop a running instance cleanly (see install.ps1 for why not Stop-Process first) ---
if (Get-Process TabTower -ErrorAction SilentlyContinue) {
    Write-Host "Stopping the running TabTower instance..."
    if (Test-Path $exe) { Invoke-TabTower $exe 'quit' | Out-Null }
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline -and (Get-Process TabTower -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 250
    }
    Get-Process TabTower -ErrorAction SilentlyContinue | Stop-Process -Force
}

# --- 2. Remove the Claude Code hooks (while the exe still exists) ---
if (Test-Path $exe) {
    Write-Host "Removing the Claude Code hooks..."
    (Invoke-TabTower $exe 'uninstall-hooks').Output | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Warning "$exe not found - if hooks are still registered, remove the TabTower entries from ~/.claude/settings.json manually."
}

# --- 3. Remove the VSCode extension ---
if (Get-Command code -ErrorAction SilentlyContinue) {
    & code --uninstall-extension tabtower.tabtower-connector 2>&1 | Out-Null
    Write-Host "VS Code extension removed (if it was installed)."
} else {
    Write-Warning "VS Code 'code' command not found - remove the 'TabTower Connector' extension from VS Code manually if present."
}

# --- 4. Remove the user-PATH entry (only that entry: the rest of the value and its registry
#        type stay exactly as they were, and nothing is written when the entry is not there) ---
if ((Edit-UserPath -Entry $InstallDir -Remove) -eq 'removed') { Send-EnvironmentChanged }

# --- 5. Remove the auto-start value ---
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TabTower' -ErrorAction SilentlyContinue

# --- 6. Delete the install dir (move out of it first in case we run from inside it) ---
if (Test-Path $InstallDir) {
    Set-Location $env:USERPROFILE
    Remove-Item -Path $InstallDir -Recurse -Force
    Write-Host "Removed $InstallDir"
}

# --- 7. User settings ---
$configDir = Join-Path $env:APPDATA 'TabTower'
if ($PurgeConfig) {
    if (Test-Path $configDir) { Remove-Item -Path $configDir -Recurse -Force; Write-Host "Removed $configDir" }
} elseif (Test-Path $configDir) {
    Write-Host "User settings kept at $configDir (re-run with -PurgeConfig to remove them too)."
}

Write-Host ""
Write-Host "TabTower uninstalled."
