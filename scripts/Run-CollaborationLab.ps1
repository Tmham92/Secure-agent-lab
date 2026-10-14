$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$checks = Join-Path $repoRoot 'tests/SecureAgentLab.CollaborationChecks/bin/Release/net10.0/SecureAgentLab.CollaborationChecks.dll'
if (!(Test-Path -LiteralPath $checks)) { throw 'Build the Release solution first.' }
. (Join-Path $PSScriptRoot 'DemoOutput.ps1')
$presentation = Start-LabPresentation -Case 'phase8' -Directory (Join-Path $repoRoot 'artifacts/phase8/latest')
$demoSucceeded = $false
try {
    Write-LabStage $presentation 'Run security scenarios and verify measured effects'
    & dotnet $checks --demo | ForEach-Object { Write-LabResult $_ }
    if ($LASTEXITCODE -ne 0) { throw 'Collaboration demo failed; no successful evaluation should be claimed.' }
    $demoSucceeded = $true
} finally { Complete-LabPresentation $presentation $demoSucceeded }
