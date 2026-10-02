# SessionDeck release script - one command from committed code to a published GitHub release.
# Usage:
#   .\release.ps1            # full release of the version in SessionDeck.csproj
#   .\release.ps1 -DryRun    # everything except the sync-commit and the actual release
#
# The version in SessionDeck.csproj is the single source of truth. The script:
#   guards (clean tree, main, unreleased version) -> syncs the hook-script version header
#   -> publishes self-contained -> runs the install-hooks tests against the published exe
#   -> packages the vsix only if the extension changed -> zips -> prepends CHANGELOG.md
#   -> deletes every older release -> gh release create.
# PowerShell 5.1 compatible.
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
Set-Location $repo
$gh = "C:\Program Files\GitHub CLI\gh.exe"
if (-not (Test-Path $gh)) { $gh = 'gh' }   # fall back to PATH on other machines

function Fail([string]$msg) { Write-Host "RELEASE BLOCKED: $msg" -ForegroundColor Red; exit 1 }
function Step([string]$msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }

# --- Guards -------------------------------------------------------------------
Step "Preflight checks"

if (git status --porcelain) { Fail "working tree is not clean - commit or stash first." }
$branch = git rev-parse --abbrev-ref HEAD
if ($branch -ne 'main') { Fail "on branch '$branch' - releases are cut from main." }

$csproj = Get-Content (Join-Path $repo 'SessionDeck.csproj') -Raw
if ($csproj -notmatch '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>') { Fail "no <Version> in SessionDeck.csproj" }
$ver = $Matches[1]
$tag = "v$ver"

# Tags are never deleted any more (see the policy below), so nothing to prune.
git fetch --tags --quiet
if (git tag --list $tag) { Fail "tag $tag already exists - bump <Version> in SessionDeck.csproj first." }

# Release policy: exactly ONE release on GitHub, always the current version. Every older
# release is deleted so the page never accumulates self-contained zips nobody downloads.
# Their TAGS survive - a tag is the only way back to an exact past build, it costs nothing
# on the releases page, and it keeps the notes baseline below honest however many releases
# have come and gone. The accumulated history lives in CHANGELOG.md, in the repo, because a
# release page that gets deleted cannot hold it.
$verObj = [version]$ver
$semverTags = @(git tag --list 'v*' | Where-Object { $_ -match '^v[0-9]+\.[0-9]+\.[0-9]+$' })
$newer = @($semverTags | Where-Object { [version]($_.TrimStart('v')) -gt $verObj })
if ($newer) { Fail "$($newer -join ', ') already tagged and newer than $ver - releasing this would replace a newer release." }

$releases = @(& $gh release list --json tagName --jq '.[].tagName' 2>$null)

# Notes baseline: the newest tag below this version. Tags outlive releases, so this stays
# correct no matter how many releases have been deleted (the old release-derived baseline
# shrank the notes on every patch - each replacement destroyed its own baseline).
$prevTag = @($semverTags |
    Where-Object { [version]($_.TrimStart('v')) -lt $verObj } |
    Sort-Object { [version]($_.TrimStart('v')) }) | Select-Object -Last 1

Write-Host "  version: $ver  (notes baseline: $(if ($prevTag) { $prevTag } else { 'none' }))"
if ($releases) { Write-Host "  will delete release(s): $($releases -join ', ')  (tags kept)" }

# --- Sync the hook script's version header (BOM must survive - PS 5.1 + non-ASCII comments) ---
Step "Hook script version header"
$hookPath = Join-Path $repo 'hooks\sessiondeck-hook.ps1'
$hookText = [IO.File]::ReadAllText($hookPath)
if ($hookText -notmatch '(?m)^# Version: (\S+)') { Fail "no '# Version:' header in the hook script." }
$hookVer = $Matches[1]
if ($hookVer -ne $ver) {
    if ($DryRun) {
        Write-Host "  would sync $hookVer -> $ver (+ commit)"
    } else {
        $hookText = $hookText -replace '(?m)^# Version: \S+', "# Version: $ver"
        [IO.File]::WriteAllText($hookPath, $hookText, [Text.UTF8Encoding]::new($true))
        $bytes = [IO.File]::ReadAllBytes($hookPath)
        if (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) { Fail "BOM lost while syncing the hook header." }
        git add $hookPath
        git commit -m "chore: sync hook script version header to $ver" --quiet
        Write-Host "  synced $hookVer -> $ver and committed."
    }
} else {
    Write-Host "  already $ver."
}

# --- Build & test ---------------------------------------------------------------
Step "Publish (self-contained, one file per assembly)"
$pubDir = Join-Path $repo 'bin\Release\net10.0-windows\win-x64\publish'
# Incremental publish silently drops Content files (hooks\) from the output dir
# once they were published before (MSBuild up-to-date tracking) - always start fresh.
if (Test-Path $pubDir) { Remove-Item $pubDir -Recurse -Force }
# Deliberately NOT PublishSingleFile. Bundling the runtime produced a 140MB SessionDeck.exe,
# and every install rewrote all of it: explorer.exe then burned a whole core on 200,000+ soft
# page faults per second and stalled the machine for about a minute, measured on eight installs.
# Spread over ~200 files the same upgrade rewrites only the assemblies
# that actually changed - typically SessionDeck.dll alone - and install.ps1 skips the rest by
# content hash. Still self-contained, so the zip needs no .NET runtime on the target machine.
dotnet publish -c Release -r win-x64 --self-contained --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Fail "dotnet publish failed." }
$pubExe = Join-Path $pubDir 'SessionDeck.exe'

$exeVer = ((Get-Item $pubExe).VersionInfo.ProductVersion -split '\+')[0]
if ($exeVer -ne $ver) { Fail "published exe reports $exeVer, expected $ver." }
$bytes = [IO.File]::ReadAllBytes((Join-Path $pubDir 'hooks\sessiondeck-hook.ps1'))
if (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) { Fail "published hook script lost its BOM." }

Step "install-hooks tests against the published exe"
powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo 'tests\install-hooks.tests.ps1') -Exe $pubExe
if ($LASTEXITCODE -ne 0) { Fail "tests failed." }

# --- VSCode extension ------------------------------------------------------------
Step "VSCode extension"
$extDir = Join-Path $repo 'vscode-extension'
$pkg = Get-Content (Join-Path $extDir 'package.json') -Raw | ConvertFrom-Json
$extVer = $pkg.version
$vsix = Join-Path $extDir "sessiondeck-connector-$extVer.vsix"

$extChanged = $true
if ($prevTag) {
    git diff --quiet $prevTag HEAD -- vscode-extension/src vscode-extension/package.json
    $extChanged = ($LASTEXITCODE -ne 0)
}
if ($extChanged -and $prevTag -and -not $DryRun) {
    # The extension changed but its version didn't -> the installed extension would look
    # identical to the old one. Block: bump vscode-extension/package.json first.
    $prevPkg = git show "${prevTag}:vscode-extension/package.json" | ConvertFrom-Json
    if ($prevPkg.version -eq $extVer) { Fail "vscode-extension changed since $prevTag but its package.json version is still $extVer - bump it." }
}
if ($extChanged -or -not (Test-Path $vsix)) {
    Write-Host "  packaging vsix $extVer..."
    Push-Location $extDir
    # npm/vsce write warnings to stderr; under EAP=Stop PS 5.1 turns a merged (2>&1)
    # native stderr line into a terminating NativeCommandError even on success.
    # The Test-Path below is the real success check.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    npm install --silent 2>&1 | Out-Null
    npx @vscode/vsce package 2>&1 | Select-Object -Last 1
    $ErrorActionPreference = $prevEap
    Pop-Location
    if (-not (Test-Path $vsix)) { Fail "vsce did not produce $vsix" }
} else {
    Write-Host "  unchanged since $prevTag - reusing sessiondeck-connector-$extVer.vsix"
}

# --- Stage & zip -----------------------------------------------------------------
Step "Package zip"
$stage = Join-Path $repo "publish\SessionDeck-$ver-win-x64"
$zip = "$stage.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
# The whole publish directory, not just the exe: without single-file the runtime assemblies
# and SessionDeck.dll sit beside it and the app does not start without them. hooks\ is in
# there too, as a Content item of the project.
Copy-Item (Join-Path $pubDir '*') $stage -Recurse
Copy-Item $vsix $stage
Copy-Item (Join-Path $repo 'install.ps1'), (Join-Path $repo 'uninstall.ps1') $stage
Compress-Archive -Path "$stage\*" -DestinationPath $zip -Force
Write-Host "  $zip ($([math]::Round((Get-Item $zip).Length/1MB)) MB)"

# --- Release notes + publish -------------------------------------------------------
Step "Release notes"
$notesFile = Join-Path $env:TEMP "sessiondeck-release-notes-$ver.md"
$changes = if ($prevTag) { git log "$prevTag..HEAD" --no-merges --format='- %s' } else { @('- initial packaged release') }
@"
**Requirements:** Windows 10 or 11 | VS Code with the Claude Code extension | no .NET runtime needed, this build is self-contained.

### Install

1. Download ``SessionDeck-$ver-win-x64.zip`` below, then **unblock it before extracting** - Windows marks every downloaded file, and the mark spreads to everything you extract out of it:

   ``````powershell
   Unblock-File .\SessionDeck-$ver-win-x64.zip
   ``````

   (Already extracted? ``Get-ChildItem -Recurse | Unblock-File`` inside the folder does the same.)

2. Extract it anywhere and run the installer - no admin rights, everything is per-user:

   ``````powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
   ``````

It installs the app, the VS Code extension and the Claude Code hooks, then starts the deck. **Upgrading** from any earlier version is the same two steps; your settings survive. Full details in the [README](https://github.com/eyalBPM/SessionDeck#getting-started).

Versions in this zip: app $ver | extension $extVer | hooks $ver

### Changes since $(if ($prevTag) { $prevTag } else { 'the beginning' })

$($changes -join "`n")

Every earlier version: [CHANGELOG.md](https://github.com/eyalBPM/SessionDeck/blob/main/CHANGELOG.md).
Only this release is kept on the Releases page, but every version's tag survives - ``git checkout v<version>`` rebuilds any of them.
"@ | Set-Content -Path $notesFile -Encoding UTF8
Get-Content $notesFile | ForEach-Object { "  | $_" }

# --- CHANGELOG ---------------------------------------------------------------------
# The releases page holds one release; this file holds the rest. Prepended here, before
# the push, so the tag gh creates already points at a commit that documents itself.
Step "CHANGELOG.md"
$clPath = Join-Path $repo 'CHANGELOG.md'
$clMarker = '<!-- new releases are inserted directly below this line -->'
$clText = [IO.File]::ReadAllText($clPath)
if (-not $clText.Contains($clMarker)) { Fail "CHANGELOG.md is missing its insertion marker." }
# No trailing newline: the text after the marker already starts with a blank line.
$clEntry = "## $tag - $(Get-Date -Format 'yyyy-MM-dd')`r`n`r`n$($changes -join "`r`n")"
if ($DryRun) {
    Write-Host "  would prepend:"
    $clEntry -split "`r`n" | ForEach-Object { "  | $_" }
} else {
    # .Replace, not -replace: a commit subject containing $1 or $& would be eaten by the
    # regex replacement operator.
    [IO.File]::WriteAllText($clPath, $clText.Replace($clMarker, "$clMarker`r`n`r`n$clEntry"), [Text.UTF8Encoding]::new($false))
    git add $clPath
    git commit -m "docs: changelog for $tag" --quiet
    Write-Host "  prepended $tag and committed."
}

if ($DryRun) {
    Step "DryRun - stopping before: git push, delete [$($releases -join ', ')], gh release create $tag"
    exit 0
}

Step "Publish"
git push origin main
# Delete every existing release - one release on the page, always the current one. No
# --cleanup-tag: the tags are the version history and the notes baseline.
foreach ($old in $releases) {
    Write-Host "  deleting release $old (tag kept)"
    & $gh release delete $old --yes
    if ($LASTEXITCODE -ne 0) { Fail "failed to delete release $old." }
}
& $gh release create $tag $zip --title $tag --notes-file $notesFile --latest
if ($LASTEXITCODE -ne 0) { Fail "gh release create failed." }
Write-Host "`nDone." -ForegroundColor Green
