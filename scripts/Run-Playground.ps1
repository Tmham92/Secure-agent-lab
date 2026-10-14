$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/SecureAgentLab.Playground/SecureAgentLab.Playground.csproj'
dotnet build (Join-Path $PSScriptRoot '../SecureAgentLab.slnx') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'Agent Security Lab: http://127.0.0.1:5080 (Ctrl+C to stop)'
dotnet run --project $project --configuration Release --no-build
exit $LASTEXITCODE

