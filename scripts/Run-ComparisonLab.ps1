[CmdletBinding()]
param([ValidateSet('all','portable','approval','scope','actions','files','injection','retry','audit','version','quota','retry-limit','isolation','containment')][string]$Scenario = 'all')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $repo 'src/SecureAgentLab.Comparisons/bin/Release/net10.0/SecureAgentLab.Comparisons.dll'
$evidencePaths = @()
if ($Scenario -notin @('isolation','containment')) {
    if (!(Test-Path -LiteralPath $dll)) { throw 'Build the Release solution first.' }
    $portable = if ($Scenario -eq 'all') { 'portable' } else { $Scenario }
    $output = & dotnet $dll $portable
    if ($LASTEXITCODE -ne 0) { throw 'Portable comparison failed; do not claim the full suite passed.' }
    $output | Out-Host
    $line = @($output | Where-Object { $_ -match '^Comparison pairs: .* Evidence: ' })[-1]
    if (!$line) { throw 'Portable summary path missing.' }
    $evidencePaths += ($line -replace '^.* Evidence: ','')
}
if ($Scenario -in @('all','isolation','containment')) {
    $container = if ($Scenario -eq 'all') { 'both' } else { $Scenario }
    $result = & (Join-Path $PSScriptRoot 'Run-ContainerComparisons.ps1') -Scenario $container
    $evidencePaths += $result.ComparisonEvidence
}
$suite = Join-Path $repo ('artifacts/comparisons/suite-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $suite -Force | Out-Null
$index = @('# Comparison suite overview','','| Scenario | Unsafe | Secure | Evidence |','|---|---|---|---|')
$pairs = @()
foreach ($path in $evidencePaths) {
    $records = @(Get-Content -LiteralPath (Join-Path $path 'comparisons.json') -Raw | ConvertFrom-Json)
    foreach ($record in $records) {
        if (!$record.Passed) { throw 'Evidence contains a failed comparison.' }
        $pairs += $record
        $relative = '../' + (Split-Path $path -Leaf) + '/summary.md'
        $index += "| $($record.Scenario) | failure observed | prevented; positive control passed | [Measured effects]($relative) |"
    }
}
if ($Scenario -eq 'all' -and $pairs.Count -ne 12) { throw 'All-comparisons suite must contain exactly 12 pairs.' }
$index | Set-Content -LiteralPath (Join-Path $suite 'summary.md')
$pairs | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $suite 'comparisons.json')
Write-Host "Comparison overview ($($pairs.Count) pairs): $(Join-Path $suite 'summary.md')"
Write-Host "Selected comparison suite completed: $Scenario"
