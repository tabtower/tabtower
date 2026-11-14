<#
.SYNOPSIS
  Tests for the "still running in another process" line on a session card (0.11.63), without
  starting TabTower itself.

.DESCRIPTION
  A card click on a session whose process is still running, with no tab of it found in its window,
  starts nothing (0.11.62; the registry side is covered by the probe stage of
  tests/session-recovery.tests.ps1). Builds tests/liveresume/LiveResumeHarness, which references the
  app, and runs its self-test on a real WorkspaceCardView rendered offscreen:
    - the refusal returns "not sent" with the reason the status line shows
    - the card draws the line on that session's row only, with the reason in its tooltip
    - the next request to open the session clears it; a closed session shows none
  Nothing is shown on screen and no session is touched.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\live-resume.tests.ps1
#>
$proj = Join-Path $PSScriptRoot 'liveresume\LiveResumeHarness\LiveResumeHarness.csproj'
$exe = Join-Path $PSScriptRoot 'liveresume\LiveResumeHarness\bin\Debug\net10.0-windows\LiveResumeHarness.exe'
dotnet build $proj -c Debug -v:q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL  harness build'; exit 1 }
& $exe selftest
exit $LASTEXITCODE
