[CmdletBinding()]
param([int]$Port = 5193, [switch]$Live, [string]$Model)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
$artifactRun = Start-LabArtifactRun -Repository $repo -Case 'model-demo'
try {
    $proposalFile = Join-Path $repo 'fixtures/model/adversarial-proposals.json'
    if ($Live) {
        if (!$Model -or !$env:OPENAI_API_KEY) { throw 'Live mode requires -Model and host OPENAI_API_KEY. No credentials are needed in offline mode.' }
        $dll = Join-Path $repo 'src/SecureAgentLab.ModelHost/bin/Release/net10.0/SecureAgentLab.ModelHost.dll'
        if (!(Test-Path -LiteralPath $dll)) { throw 'Build the Release solution first.' }
        $previousModel = $env:LAB_OPENAI_MODEL
        try {
            $env:LAB_OPENAI_MODEL = $Model
            $json = (& dotnet $dll) -join "`n"
            if ($LASTEXITCODE -ne 0) { throw 'Model generation failed; no proposals submitted.' }
            $folder = $artifactRun.DirectoryPath
            New-Item -ItemType Directory -Path $folder -Force | Out-Null
            $proposalFile = Join-Path $folder 'proposals.json'
            [IO.File]::WriteAllText($proposalFile, $json, [Text.UTF8Encoding]::new($false))
        }
        finally { $env:LAB_OPENAI_MODEL = $previousModel }
        Write-Host 'One live model response validated. Submitting through the authenticated gateway.'
    }
    else { Write-Host 'Offline adversarial-output fixture: no model call or API key required.' }
    & (Join-Path $PSScriptRoot 'Run-Lab.ps1') -Port $Port -ModelProposalsFile $proposalFile -ExpectedDocumentReads $(if ($Live) { -1 } else { 1 })

} finally { $artifactRun.Dispose() }
