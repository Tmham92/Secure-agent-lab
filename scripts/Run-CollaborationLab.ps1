$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$checks = Join-Path $repoRoot 'tests/SecureAgentLab.CollaborationChecks/bin/Release/net10.0/SecureAgentLab.CollaborationChecks.dll'
if (!(Test-Path -LiteralPath $checks)) { throw 'Build the Release solution first.' }
& dotnet $checks --demo
if ($LASTEXITCODE -ne 0) { throw 'Collaboration demo failed; no successful evaluation should be claimed.' }
