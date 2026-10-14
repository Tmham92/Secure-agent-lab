[CmdletBinding()]
param([int]$Port = 5192)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
. (Join-Path $PSScriptRoot 'DemoOutput.ps1')
$artifactRun = Start-LabArtifactRun -Repository $repo -Case 'document-demo'
$presentation = Start-LabPresentation -Case $artifactRun.CaseName -Directory $artifactRun.DirectoryPath -RunNumber $artifactRun.RunNumber
$demoSucceeded = $false
try {
    $documents = $artifactRun.DirectoryPath
    & (Join-Path $PSScriptRoot 'New-DocumentFixtures.ps1') -Destination $documents
    Write-Host 'Phase 5: host-owned pinned synthetic files; worker receives IDs and short-lived credential only.'
    Write-LabStage $presentation 'Submit untrusted requests through the authenticated gateway; review the nested gateway evidence'
    & (Join-Path $PSScriptRoot 'Run-Lab.ps1') -PresentationContext $presentation -Port $Port -DocumentRoot $documents
    Write-Host 'File-backed document demo completed. Run DocumentChecks for cross-task, path and failure cases.'

    $demoSucceeded = $true
} finally { try { Complete-LabPresentation $presentation $demoSucceeded } finally { $artifactRun.Dispose() } }
