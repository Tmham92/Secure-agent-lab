function Start-LabArtifactRun {
    param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Case)
    $assembly = Join-Path $Repository 'src/SecureAgentLab.Core/bin/Release/net10.0/SecureAgentLab.Core.dll'
    if (!(Test-Path -LiteralPath $assembly)) { throw 'Build the Release solution first.' }
    [Reflection.Assembly]::LoadFrom($assembly) | Out-Null
    $run = [SecureAgentLab.Core.Diagnostics.ArtifactRun]::Start($Repository, $Case)
    Write-Host "Artifact case $Case run #$($run.RunNumber): $($run.DirectoryPath)"
    return $run
}
