# Tests for the user-PATH edit of install.ps1 and uninstall.ps1 (Edit-UserPath).
#
# The installer must add one entry to the user PATH and the uninstaller must take that one
# entry out again, and neither may change anything else about the value: not the text of the
# other entries (a %USERPROFILE%-style entry stays unexpanded) and not its registry type
# (REG_EXPAND_SZ stays REG_EXPAND_SZ).
#
# The function is loaded out of each script's own text. The scripts are parsed, never run:
# run for real they stop the app, copy files and register hooks. Every write goes to a scratch
# key under HKCU\Software that this file creates and deletes again. The real user PATH is
# only read, twice, to confirm at the end that its text and type did not move.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\user-path.tests.ps1
#
# Windows PowerShell 5.1 and PowerShell 7. Exit code 0 when every check passes.

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$pass = 0; $fail = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Host "  PASS  $name" }
    else     { $script:fail++; Write-Host "  FAIL  $name -- $detail" }
}

$ExpandString = [Microsoft.Win32.RegistryValueKind]::ExpandString
$String       = [Microsoft.Win32.RegistryValueKind]::String
$hkcu         = [Microsoft.Win32.Registry]::CurrentUser
$scratch      = 'Software\TabTowerTest-' + [guid]::NewGuid().ToString('N').Substring(0, 12)

# The Path value of a key exactly as stored: its registry type and its text with nothing
# expanded. $null when the key or the value does not exist.
function Read-PathValue([string]$SubKey) {
    $key = $hkcu.OpenSubKey($SubKey, $false)
    if (-not $key) { return $null }
    try {
        if ($key.GetValueNames() -notcontains 'Path') { return $null }
        $text = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        return [pscustomobject]@{ Kind = $key.GetValueKind('Path'); Text = (@($text) -join "`n") }
    } finally { $key.Dispose() }
}

# Same type and the same characters, compared one to one (no case folding, no culture).
function Test-SameValue($a, $b) {
    if ($null -eq $a -or $null -eq $b) { return ($null -eq $a -and $null -eq $b) }
    return ($a.Kind -eq $b.Kind) -and [string]::Equals($a.Text, $b.Text, [StringComparison]::Ordinal)
}

function Show-Value($v) { if ($null -eq $v) { '<no value>' } else { "[$($v.Kind)] '$($v.Text)'" } }

# A fresh sub-key of the scratch key, with a Path value when -Text is given and a neighbouring
# value that no edit may touch.
function New-Case([string]$Name, $Text, $Kind) {
    $sub = "$scratch\$Name"
    $key = $hkcu.CreateSubKey($sub)
    try {
        if ($null -ne $Text) { $key.SetValue('Path', $Text, $Kind) }
        $key.SetValue('Neighbour', '%TEMP%\left-alone', $ExpandString)
    } finally { $key.Dispose() }
    return $sub
}

function Test-NeighbourIntact([string]$SubKey) {
    $key = $hkcu.OpenSubKey($SubKey, $false)
    try {
        $text = $key.GetValue('Neighbour', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        return ($key.GetValueKind('Neighbour') -eq $ExpandString) -and ($text -ceq '%TEMP%\left-alone')
    } finally { $key.Dispose() }
}

function Get-ScriptAst([string]$Path) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { throw "$Path does not parse: $($errors[0].Message)" }
    return $ast
}

function Get-FunctionAst($Ast, [string]$Name) {
    $found = @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name }, $false))
    if ($found.Count -ne 1) { throw "expected one function $Name, found $($found.Count)" }
    return $found[0]
}

function Get-Calls($Ast, [string]$Name) {
    return @($Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq $Name }, $true))
}

function Get-ParameterNames($Call) {
    return @($Call.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] } | ForEach-Object { $_.ParameterName })
}

$installAst   = Get-ScriptAst (Join-Path $repo 'install.ps1')
$uninstallAst = Get-ScriptAst (Join-Path $repo 'uninstall.ps1')

# The installer's own copy does the adding, the uninstaller's own copy does the removing:
# what is tested is the text each script ships with.
Set-Item function:Install-EditUserPath     (Get-FunctionAst $installAst   'Edit-UserPath').Body.GetScriptBlock()
Set-Item function:Uninstall-EditUserPath   (Get-FunctionAst $uninstallAst 'Edit-UserPath').Body.GetScriptBlock()
Set-Item function:Install-EnvironmentNote  (Get-FunctionAst $installAst   'Send-EnvironmentChanged').Body.GetScriptBlock()

# Not the real install folder, and not a folder that exists.
$entry = Join-Path $env:LOCALAPPDATA 'Programs\TabTowerTestEntry'

$realBefore = Read-PathValue 'Environment'

try {
    [void]$hkcu.CreateSubKey($scratch)

    Write-Host "The scripts themselves"
    $editA = (Get-FunctionAst $installAst 'Edit-UserPath').Extent.Text -replace "`r", ''
    $editB = (Get-FunctionAst $uninstallAst 'Edit-UserPath').Extent.Text -replace "`r", ''
    Check 'Edit-UserPath is the same text in both scripts' ($editA -ceq $editB)
    $noteA = (Get-FunctionAst $installAst 'Send-EnvironmentChanged').Extent.Text -replace "`r", ''
    $noteB = (Get-FunctionAst $uninstallAst 'Send-EnvironmentChanged').Extent.Text -replace "`r", ''
    Check 'Send-EnvironmentChanged is the same text in both scripts' ($noteA -ceq $noteB)
    foreach ($pair in @(@('install.ps1', $installAst), @('uninstall.ps1', $uninstallAst))) {
        # The two .NET calls that expand the value and drop its type must not come back.
        $old = @($pair[1].FindAll({ param($n) $n -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
                "$($n.Member)" -match '^(Get|Set)EnvironmentVariable$' }, $true))
        Check "$($pair[0]) no longer calls Get/SetEnvironmentVariable" ($old.Count -eq 0) "$($old.Count) call(s)"
        $withKey = @(Get-Calls $pair[1] 'Edit-UserPath' | Where-Object { (Get-ParameterNames $_) -contains 'SubKey' })
        Check "$($pair[0]) never aims Edit-UserPath at another key" ($withKey.Count -eq 0)
        $withWindow = @(Get-Calls $pair[1] 'Send-EnvironmentChanged' | Where-Object { $_.CommandElements.Count -ne 1 })
        Check "$($pair[0]) calls Send-EnvironmentChanged with no argument" ($withWindow.Count -eq 0)
    }
    $installCalls = @(Get-Calls $installAst 'Edit-UserPath' | ForEach-Object { $_.Extent.Text })
    Check 'install.ps1 edits the PATH in two places: the former name out, the install folder in' `
        ($installCalls.Count -eq 2 -and ($installCalls -ccontains 'Edit-UserPath -Entry $legacyDir -Remove') -and
         ($installCalls -ccontains 'Edit-UserPath -Entry $InstallDir')) ($installCalls -join ' | ')
    $uninstallCalls = @(Get-Calls $uninstallAst 'Edit-UserPath' | ForEach-Object { $_.Extent.Text })
    Check 'uninstall.ps1 edits the PATH in one place: the install folder out' `
        ($uninstallCalls.Count -eq 1 -and $uninstallCalls[0] -ceq 'Edit-UserPath -Entry $InstallDir -Remove') ($uninstallCalls -join ' | ')

    # ---- install, install again, uninstall, uninstall again: over each kind of starting value ----
    $withVariables = '%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;%LOCALAPPDATA%\Programs\Some Tool\bin;C:\Tools'
    $long = (1..180 | ForEach-Object { "%USERPROFILE%\tools\folder-number-$_\bin" }) -join ';'
    $starts = @(
        @{ Name = 'expand-string';        Kind = $ExpandString; Text = $withVariables }
        @{ Name = 'expand-trailing-sep';  Kind = $ExpandString; Text = $withVariables + ';' }
        @{ Name = 'expand-odd-spelling';  Kind = $ExpandString; Text = ';C:\Tools;;"C:\Quoted Folder";  %TEMP%\x ;c:\TOOLS\' }
        @{ Name = 'expand-long';          Kind = $ExpandString; Text = $long }
        @{ Name = 'plain-string';         Kind = $String;       Text = 'C:\Tools;D:\More Tools\bin' }
        @{ Name = 'plain-with-percent';   Kind = $String;       Text = '%USERPROFILE%\bin;C:\Tools' }
        @{ Name = 'expand-one-entry';     Kind = $ExpandString; Text = '%USERPROFILE%\bin' }
        @{ Name = 'expand-empty';         Kind = $ExpandString; Text = '' }
        @{ Name = 'plain-empty';          Kind = $String;       Text = '' }
    )
    foreach ($s in $starts) {
        Write-Host "Case $($s.Name): a $($s.Kind) value of $($s.Text.Length) characters"
        $sub = New-Case $s.Name $s.Text $s.Kind
        $start = Read-PathValue $sub

        $r = Install-EditUserPath -Entry $entry -SubKey $sub
        $afterAdd = Read-PathValue $sub
        $expected = if ($s.Text.Length -eq 0) { $entry } elseif ($s.Text.EndsWith(';')) { $s.Text + $entry + ';' } else { $s.Text + ';' + $entry }
        Check 'install adds the entry' ($r -eq 'added') "returned $r"
        Check 'the type is kept' ($afterAdd.Kind -eq $s.Kind) (Show-Value $afterAdd)
        Check 'the text before it is unchanged, character for character' `
            ([string]::Equals($afterAdd.Text, $expected, [StringComparison]::Ordinal)) (Show-Value $afterAdd)
        if ($s.Text.Contains('%')) {
            Check 'no variable was expanded' ($afterAdd.Text.StartsWith($s.Text, [StringComparison]::Ordinal) -and
                $afterAdd.Text.Contains('%')) (Show-Value $afterAdd)
        }

        $r = Install-EditUserPath -Entry $entry -SubKey $sub
        $afterSecond = Read-PathValue $sub
        Check 'a second install adds nothing' ($r -eq 'present' -and (Test-SameValue $afterSecond $afterAdd)) "returned $r, $(Show-Value $afterSecond)"
        $count = @($afterSecond.Text.Split(';') | Where-Object { $_ -ceq $entry }).Count
        Check 'the entry is there exactly once' ($count -eq 1) "$count time(s)"

        $r = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
        $afterRemove = Read-PathValue $sub
        Check 'uninstall removes the entry' ($r -eq 'removed' -and -not $afterRemove.Text.Contains('TabTowerTestEntry')) "returned $r, $(Show-Value $afterRemove)"
        Check 'what is left is the starting value: same type, same characters' (Test-SameValue $afterRemove $start) "$(Show-Value $afterRemove) against $(Show-Value $start)"

        $r = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
        Check 'a second uninstall changes nothing' ($r -eq 'absent' -and (Test-SameValue (Read-PathValue $sub) $start)) "returned $r"
        Check 'the neighbouring value was never touched' (Test-NeighbourIntact $sub)
    }

    Write-Host "Case no-value: the key has no Path value at all"
    $sub = New-Case 'no-value' $null $null
    $r = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
    Check 'uninstall with nothing to remove creates no value' ($r -eq 'absent' -and $null -eq (Read-PathValue $sub)) "returned $r, $(Show-Value (Read-PathValue $sub))"
    $r = Install-EditUserPath -Entry $entry -SubKey $sub
    $v = Read-PathValue $sub
    Check 'install creates the value with the entry alone' ($r -eq 'added' -and $v.Text -ceq $entry) "returned $r, $(Show-Value $v)"
    Check 'a value this script creates is REG_EXPAND_SZ, the type Windows uses for a user Path' ($v.Kind -eq $ExpandString) (Show-Value $v)
    $r = Install-EditUserPath -Entry $entry -SubKey $sub
    Check 'a second install adds nothing' ($r -eq 'present' -and (Read-PathValue $sub).Text -ceq $entry) "returned $r"
    $r = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
    $v = Read-PathValue $sub
    Check 'uninstall leaves an empty value of the same type (it does not delete a registry value)' `
        ($r -eq 'removed' -and $null -ne $v -and $v.Text -ceq '' -and $v.Kind -eq $ExpandString) "returned $r, $(Show-Value $v)"
    Check 'the neighbouring value was never touched' (Test-NeighbourIntact $sub)

    Write-Host "Case no-key: the key itself does not exist"
    $sub = "$scratch\no-key"
    $r = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
    Check 'uninstall does not create the key' ($r -eq 'absent' -and $null -eq $hkcu.OpenSubKey($sub)) "returned $r"
    $r = Install-EditUserPath -Entry $entry -SubKey $sub
    Check 'install creates key and value' ($r -eq 'added' -and (Read-PathValue $sub).Text -ceq $entry) "returned $r"

    Write-Host "Case other-spellings: the entry is already there, written differently"
    $unexpanded = '%LOCALAPPDATA%\Programs\TabTowerTestEntry'
    $n = 0
    foreach ($spelling in @(($entry + '\'), $entry.ToUpperInvariant(), ('"' + $entry + '"'), (' ' + $entry + ' '), $unexpanded)) {
        $n++
        $text = 'C:\Tools;' + $spelling + ';%USERPROFILE%\bin'
        $sub = New-Case "spelling-$n" $text $ExpandString
        $start = Read-PathValue $sub
        $r = Install-EditUserPath -Entry $entry -SubKey $sub
        Check "install sees '$spelling' as the entry and writes nothing" ($r -eq 'present' -and (Test-SameValue (Read-PathValue $sub) $start)) "returned $r"
        $r = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
        $v = Read-PathValue $sub
        Check "uninstall removes '$spelling' and nothing else" ($r -eq 'removed' -and $v.Text -ceq 'C:\Tools;%USERPROFILE%\bin' -and $v.Kind -eq $ExpandString) "returned $r, $(Show-Value $v)"
    }
    $sub = New-Case 'unexpanded-in-plain-string' ('C:\Tools;' + $unexpanded) $String
    $r = Install-EditUserPath -Entry $entry -SubKey $sub
    Check 'in a plain string Windows expands nothing, so the unexpanded spelling is not the entry' `
        ($r -eq 'added' -and (Read-PathValue $sub).Text -ceq ('C:\Tools;' + $unexpanded + ';' + $entry)) "returned $r, $(Show-Value (Read-PathValue $sub))"

    Write-Host "Case positions: where the entry sits in the value"
    $positions = @(
        @{ Name = 'first';  Text = "$entry;C:\A;%USERPROFILE%\b"; Left = 'C:\A;%USERPROFILE%\b' }
        @{ Name = 'middle'; Text = "C:\A;$entry;%USERPROFILE%\b"; Left = 'C:\A;%USERPROFILE%\b' }
        @{ Name = 'last';   Text = "C:\A;%USERPROFILE%\b;$entry"; Left = 'C:\A;%USERPROFILE%\b' }
        @{ Name = 'twice';  Text = "$entry;C:\A;$entry";          Left = 'C:\A' }
        @{ Name = 'alone';  Text = "$entry";                      Left = '' }
    )
    foreach ($p in $positions) {
        $sub = New-Case ('position-' + $p.Name) $p.Text $ExpandString
        $r = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
        $v = Read-PathValue $sub
        Check "uninstall, entry $($p.Name)" ($r -eq 'removed' -and $v.Text -ceq $p.Left -and $v.Kind -eq $ExpandString) "returned $r, $(Show-Value $v)"
    }

    Write-Host "Case former-name: install.ps1 also takes one older folder out"
    $former = Join-Path $env:LOCALAPPDATA 'Programs\TabTowerTestFormerName'
    $sub = New-Case 'former-name' "%USERPROFILE%\bin;$former;C:\Tools" $ExpandString
    $r = Install-EditUserPath -Entry $former -Remove -SubKey $sub
    $v = Read-PathValue $sub
    Check 'only that folder leaves the value' ($r -eq 'removed' -and $v.Text -ceq '%USERPROFILE%\bin;C:\Tools' -and $v.Kind -eq $ExpandString) "returned $r, $(Show-Value $v)"
    $r = Install-EditUserPath -Entry $former -Remove -SubKey $sub
    Check 'when it is not there, nothing is written' ($r -eq 'absent' -and (Read-PathValue $sub).Text -ceq '%USERPROFILE%\bin;C:\Tools') "returned $r"

    Write-Host "Case not-text: a Path value of a type that holds no single text"
    $sub = "$scratch\not-text"
    $key = $hkcu.CreateSubKey($sub)
    $key.SetValue('Path', [string[]]@('C:\Tools', '%USERPROFILE%\bin'), [Microsoft.Win32.RegistryValueKind]::MultiString)
    $key.Dispose()
    $start = Read-PathValue $sub
    $r1 = Install-EditUserPath -Entry $entry -SubKey $sub
    $r2 = Uninstall-EditUserPath -Entry $entry -Remove -SubKey $sub
    Check 'neither script writes to it' ($r1 -eq 'unsupported' -and $r2 -eq 'unsupported' -and (Test-SameValue (Read-PathValue $sub) $start)) "returned $r1 and $r2"

    Write-Host "The notice to running programs"
    # Sent to window handle 0, which is no window: the call is compiled and made, and nothing
    # receives it. The scripts send it to every window, which this test must not do.
    $warnings = @(Install-EnvironmentNote -Window ([IntPtr]::Zero) 3>&1)
    Check 'Send-EnvironmentChanged compiles and runs without a warning' ($warnings.Count -eq 0) ($warnings -join ' | ')
}
finally {
    $hkcu.DeleteSubKeyTree($scratch, $false)
}

Write-Host "After the run"
Check 'the scratch key is gone' ($null -eq $hkcu.OpenSubKey($scratch))
Check 'the real user PATH is exactly as it was: same type, same characters' (Test-SameValue (Read-PathValue 'Environment') $realBefore)

Write-Host ""
Write-Host "==== $pass passed, $fail failed ===="
if ($fail -gt 0) { exit 1 }
