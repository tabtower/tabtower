<#
.SYNOPSIS
  Tests for scoping the elimination claim to the window (0.11.59, Services/EliminationScope.cs),
  without starting TabTower itself.

.DESCRIPTION
  Builds tests/elimination/EliminationHarness, which compiles the app's own
  Services/EliminationScope.cs, and runs its self-test. Covers the 30-10-2026 incident (a session
  stamped in one window taking, by elimination, a tab only another window showed), the own-window
  pair, a mixed two-window card, duplicate labels, and every "unknown" shape that must keep the
  old behaviour (no stamp, no mapping, an unidentified window).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\elimination-scope.tests.ps1
#>
$proj = Join-Path $PSScriptRoot 'elimination\EliminationHarness\EliminationHarness.csproj'
$exe = Join-Path $PSScriptRoot 'elimination\EliminationHarness\bin\Debug\net10.0\EliminationHarness.exe'
dotnet build $proj -c Debug -v:q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL  harness build'; exit 1 }
& $exe selftest
exit $LASTEXITCODE
