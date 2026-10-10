[CmdletBinding()]
param([int]$Port = 5192)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
$artifactRun = Start-LabArtifactRun -Repository $repo -Case 'document-demo'
try {
    $documents = $artifactRun.DirectoryPath
    & (Join-Path $PSScriptRoot 'New-DocumentFixtures.ps1') -Destination $documents
    Write-Host 'Phase 5: host-owned pinned synthetic files; worker receives IDs and short-lived credential only.'
    & (Join-Path $PSScriptRoot 'Run-Lab.ps1') -Port $Port -DocumentRoot $documents
    Write-Host 'File-backed document demo completed. Run DocumentChecks for cross-task, path and failure cases.'

} finally { $artifactRun.Dispose() }
