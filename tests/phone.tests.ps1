<#
.SYNOPSIS
  Tests for the phone page (Services/Phone), without starting TabTower itself.

.DESCRIPTION
  1. Builds tests/phone/PhoneHarness, which compiles the app's own Services/Phone sources against
     a fake deck, and runs its in-process self-test: the Host / Funnel / header / owner / pairing
     rules, the HTTP parser's limits, the pairing clock and the string tables.
  2. Starts the harness as a real server on 127.0.0.1 (hidden, no window) and checks the same
     rules over real sockets with curl.exe, then stops it.

  It never touches a running TabTower, its pipe or its config. Requests from another tailnet
  device are out of its reach; docs/phone-access.md describes that check.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\phone.tests.ps1
#>
param(
    [int]$Port = 7096,
    [int]$ControlPort = 7097
)

$ErrorActionPreference = 'Continue'
$proj = Join-Path $PSScriptRoot 'phone\PhoneHarness\PhoneHarness.csproj'
$exe = Join-Path $PSScriptRoot 'phone\PhoneHarness\bin\Debug\net10.0\PhoneHarness.exe'
$failed = 0

function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "PASS  $what" } else { Write-Host "FAIL  $what"; $script:failed++ }
}

dotnet build $proj -c Debug -v:q -nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL  harness build'; exit 1 }

& $exe selftest
Check ($LASTEXITCODE -eq 0) 'in-process self-test'

$log = Join-Path ([IO.Path]::GetTempPath()) "phone-harness-$PID.log"
$proc = Start-Process -FilePath $exe -ArgumentList 'serve', '--port', $Port, '--control', $ControlPort `
    -WindowStyle Hidden -RedirectStandardOutput $log -PassThru
try {
    $base = "http://127.0.0.1:$Port"
    for ($i = 0; $i -lt 50; $i++) {
        if ((curl.exe -s -o NUL -w '%{http_code}' -H 'Host: localhost' "$base/") -eq '200') { break }
        Start-Sleep -Milliseconds 200
    }
    function Code([string[]]$extra, [string]$path) {
        $a = @('-s', '-o', 'NUL', '-w', '%{http_code}') + $extra + @("$base$path")
        return (& curl.exe @a)
    }
    Check ((Code @('-H', 'Host: localhost') '/') -eq '200') 'GET / over a real socket'
    Check ((Code @('-H', 'Host: localhost', '-H', 'X-TabTower-Phone: 1') '/api/list') -eq '200') 'local API call with the header'
    Check ((Code @('-H', 'Host: localhost') '/api/list') -eq '403') 'API call without the header'
    Check ((Code @('-H', 'Host: rebind.example') '/') -eq '403') 'foreign Host header'
    Check ((Code @('-H', 'Host: localhost', '-H', 'Tailscale-Funnel-Request: ?1') '/') -eq '403') 'Funnel header'
    Check ((Code @('-H', 'Host: localhost', '-H', 'X-Forwarded-For: 203.0.113.9') '/') -eq '403') 'an address Tailscale does not know'
    # This PC's own tailnet name WITHOUT X-Forwarded-For is what a tcp / tls-terminated forward
    # (or an SSH / editor port forward) delivers: refused. Another machine's name: refused.
    $selfName = ''
    try { $selfName = ((tailscale status --json | ConvertFrom-Json).Self.DNSName).TrimEnd('.') } catch { }
    if ($selfName) {
        Check ((Code @('-H', "Host: $selfName", '-H', 'X-TabTower-Phone: 1') '/api/list') -eq '403') 'own tailnet name without X-Forwarded-For'
        Check ((Code @('-H', "Host: other-$selfName") '/') -eq '403') "another machine's tailnet name"
    } else { Write-Host 'SKIP  tailnet-name checks (Tailscale not running)' }
    # Bodies go through files: Windows PowerShell strips the quotes inside a native argument.
    $newBody = Join-Path ([IO.Path]::GetTempPath()) "phone-new-$PID.json"
    $bigBody = Join-Path ([IO.Path]::GetTempPath()) "phone-big-$PID.txt"
    [IO.File]::WriteAllText($newBody, '{"card":"ws:1","group":""}')
    [IO.File]::WriteAllText($bigBody, ('x' * 70000))
    Check ((Code @('-H', 'Host: localhost', '-H', 'X-TabTower-Phone: 1', '-H', 'Content-Type: application/json',
                   '--data-binary', "@$newBody") '/api/new') -eq '200') 'new session (fake deck) over a real socket'
    Check ((Code @('-H', 'Host: localhost', '-H', 'X-TabTower-Phone: 1', '--data-binary', "@$bigBody") '/api/close') -eq '413') 'oversized body'
    Remove-Item $newBody, $bigBody -ErrorAction SilentlyContinue
    # A raw request line the parser must refuse (absolute-form target).
    $tcp = [Net.Sockets.TcpClient]::new('127.0.0.1', $Port)
    $s = $tcp.GetStream()
    $b = [Text.Encoding]::ASCII.GetBytes("GET http://elsewhere/ HTTP/1.1`r`nHost: localhost`r`n`r`n")
    $s.Write($b, 0, $b.Length)
    $buf = New-Object byte[] 64; $n = $s.Read($buf, 0, 64); $tcp.Close()
    Check ([Text.Encoding]::ASCII.GetString($buf, 0, $n).StartsWith('HTTP/1.1 400')) 'absolute-form request target'
} finally {
    curl.exe -s -o NUL "http://127.0.0.1:$ControlPort/quit" -H 'Host: localhost'
    if (-not $proc.WaitForExit(5000)) { $proc.Kill() }
    Remove-Item $log -ErrorAction SilentlyContinue
}

if ($failed -gt 0) { Write-Host "`n$failed check(s) failed"; exit 1 }
Write-Host "`nall phone checks passed"
exit 0
