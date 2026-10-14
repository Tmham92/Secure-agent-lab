[CmdletBinding()]
param([int]$Port = 5193, [switch]$Live, [string]$Model)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
. (Join-Path $PSScriptRoot 'DemoOutput.ps1')
$artifactRun = Start-LabArtifactRun -Repository $repo -Case 'model-demo'
$presentation = Start-LabPresentation -Case $artifactRun.CaseName -Directory $artifactRun.DirectoryPath -RunNumber $artifactRun.RunNumber
$demoSucceeded = $false
try {
    $proposalFile = Join-Path $repo 'fixtures/model/adversarial-proposals.json'
    Write-LabStage $presentation 'Prepare untrusted proposals (offline fixture by default; live mode calls the selected model)'
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
    Write-LabStage $presentation 'Submit untrusted requests through the authenticated gateway; review the nested gateway evidence'
    & (Join-Path $PSScriptRoot 'Run-Lab.ps1') -PresentationContext $presentation -Port $Port -ModelProposalsFile $proposalFile -ExpectedDocumentReads $(if ($Live) { -1 } else { 1 })

    $demoSucceeded = $true
} finally { try { Complete-LabPresentation $presentation $demoSucceeded } finally { $artifactRun.Dispose() } }
