$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
$testRun = Start-LabArtifactRun -Repository $repo -Case 'retention-checks'
try {
    $sandbox = Join-Path $testRun.DirectoryPath 'sandbox'
    $outside = Join-Path $testRun.DirectoryPath 'outside'
    New-Item -ItemType Directory -Force $outside | Out-Null
    $canary = Join-Path $outside 'canary.txt'
    Set-Content -LiteralPath $canary 'preserve this target'
    $first = [SecureAgentLab.Core.Diagnostics.ArtifactRun]::Start($sandbox, 'probe')
    try {
        $fixedPath = $first.DirectoryPath
        Set-Content -LiteralPath (Join-Path $fixedPath 'old.txt') 'old evidence'
        $blocked = $false
        try { $unexpected = [SecureAgentLab.Core.Diagnostics.ArtifactRun]::Start($sandbox, 'probe'); $unexpected.Dispose() }
        catch { $blocked = $true }
        if (!$blocked -or !(Test-Path (Join-Path $fixedPath 'old.txt'))) { throw 'Active run was overwritten.' }
        $link = Join-Path $fixedPath 'link'
        if ($IsWindows) { New-Item -ItemType Junction -Path $link -Target $outside | Out-Null }
        else { New-Item -ItemType SymbolicLink -Path $link -Target $outside | Out-Null }
    }
    finally { $first.Dispose() }
    $second = [SecureAgentLab.Core.Diagnostics.ArtifactRun]::Start($sandbox, 'probe')
    try {
        if ($second.DirectoryPath -ne $fixedPath -or $second.RunNumber -ne 2) { throw 'Run path or counter did not remain stable.' }
        if (Test-Path (Join-Path $fixedPath 'old.txt')) { throw 'Previous evidence was retained.' }
        if (!(Test-Path -LiteralPath $canary)) { throw 'Link cleanup deleted the target.' }
        $metadata = Get-Content (Join-Path $fixedPath 'run.json') -Raw | ConvertFrom-Json
        if ($metadata.RunNumber -ne 2) { throw 'Evidence run number missing.' }
    }
    finally { $second.Dispose() }
    $rejected = $false
    try { $unexpected = [SecureAgentLab.Core.Diagnostics.ArtifactRun]::Start($sandbox, '../outside'); $unexpected.Dispose() }
    catch { $rejected = $true }
    if (!$rejected -or !(Test-Path -LiteralPath $canary)) { throw 'Traversal ownership guard failed.' }
    $redirect = Join-Path $sandbox 'artifacts/redirect'
    if ($IsWindows) { New-Item -ItemType Junction -Path $redirect -Target $outside | Out-Null }
    else { New-Item -ItemType SymbolicLink -Path $redirect -Target $outside | Out-Null }
    $rejected = $false
    try { $unexpected = [SecureAgentLab.Core.Diagnostics.ArtifactRun]::Start($sandbox, 'redirect/probe'); $unexpected.Dispose() }
    catch { $rejected = $true }
    if (!$rejected -or !(Test-Path -LiteralPath $canary)) { throw 'Linked artifact ownership path was accepted.' }
    $counter = Join-Path $sandbox 'artifacts/.runs/probe.json'
    Set-Content -LiteralPath $counter 'corrupt'
    $rejected = $false
    try { $unexpected = [SecureAgentLab.Core.Diagnostics.ArtifactRun]::Start($sandbox, 'probe'); $unexpected.Dispose() }
    catch { $rejected = $true }
    if (!$rejected) { throw 'Corrupt counter silently reset.' }
    Write-Host 'PASS fixed artifact path, replacement, run counter, active-run exclusion, link-safe cleanup/ownership and invalid path/counter rejection'
}
finally { $testRun.Dispose() }

