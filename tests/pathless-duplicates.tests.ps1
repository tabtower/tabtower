<#
.SYNOPSIS
  Tests for retiring a folderless duplicate card (0.11.44, Services/PathlessDuplicate.cs), without
  starting TabTower itself.

.DESCRIPTION
  Builds tests/pathless/PathlessHarness, which compiles the app's own Services/PathlessDuplicate.cs,
  and runs its self-test. The rule deletes a card from the deck, so the suite checks that the one
  card meant to go goes, and that loosening any single condition keeps it:
    - every condition holds, title cut from a window a foldered card owns  -> retired
    - a folder, no name, a session, hidden, LastUsedAt, UseCount, a custom title,
      a custom colour or a description, each on its own                    -> kept
    - no owner for the window, no window, a title it was not cut from,
      no connector                                                          -> kept
    - TitleIsCutFrom / WorkspaceNameFromTitle over real title shapes and near misses

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\pathless-duplicates.tests.ps1
#>
$proj = Join-Path $PSScriptRoot 'pathless\PathlessHarness\PathlessHarness.csproj'
$exe = Join-Path $PSScriptRoot 'pathless\PathlessHarness\bin\Debug\net10.0\PathlessHarness.exe'
dotnet build $proj -c Debug -v:q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL  harness build'; exit 1 }
& $exe selftest
exit $LASTEXITCODE
