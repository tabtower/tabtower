<#
.SYNOPSIS
  Tests for closing an unassociated tab by label (0.11.59, Services/TabLabelClose.cs), without
  starting TabTower itself.

.DESCRIPTION
  Builds tests/tablabel/TabLabelHarness, which compiles the app's own Services/TabLabelClose.cs, and
  runs its self-test. Covers the 30-10-2026 shape (a dead session's tab beside its successor, no
  card answering to it: the one case that sends) and every refusal: no label, no connector, an old
  extension, no tab or two tabs with the label (a truncated label included), a session answering to
  it, a live session holding a tab by elimination.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\tab-label-close.tests.ps1
#>
$proj = Join-Path $PSScriptRoot 'tablabel\TabLabelHarness\TabLabelHarness.csproj'
$exe = Join-Path $PSScriptRoot 'tablabel\TabLabelHarness\bin\Debug\net10.0\TabLabelHarness.exe'
dotnet build $proj -c Debug -v:q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL  harness build'; exit 1 }
& $exe selftest
exit $LASTEXITCODE
