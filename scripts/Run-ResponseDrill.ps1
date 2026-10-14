$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$checks = Join-Path $repoRoot 'tests/SecureAgentLab.DurableChecks/bin/Release/net10.0/SecureAgentLab.DurableChecks.dll'
if (!(Test-Path -LiteralPath $checks)) { throw 'Build the Release solution first.' }
. (Join-Path $PSScriptRoot 'DemoOutput.ps1')
$presentation = Start-LabPresentation -Case 'phase7' -Directory (Join-Path $repoRoot 'artifacts/phase7/latest')
$demoSucceeded = $false
try {
    Write-LabStage $presentation 'Run security scenarios and verify measured effects'
    & dotnet $checks --drill | ForEach-Object { Write-LabResult $_ }
    if ($LASTEXITCODE -ne 0) { throw 'Response drill failed. Inspect the failures and artifacts/phase7 evidence.' }
    $demoSucceeded = $true
} finally { Complete-LabPresentation $presentation $demoSucceeded }
