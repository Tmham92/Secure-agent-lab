$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$checks = Join-Path $repoRoot 'tests/SecureAgentLab.DurableChecks/bin/Release/net10.0/SecureAgentLab.DurableChecks.dll'
if (!(Test-Path -LiteralPath $checks)) { throw 'Build the Release solution first.' }
& dotnet $checks --drill
if ($LASTEXITCODE -ne 0) { throw 'Response drill failed. Inspect the failures and artifacts/phase7 evidence.' }
