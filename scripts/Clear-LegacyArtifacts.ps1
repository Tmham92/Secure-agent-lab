# One-time migration of known generated histories. Latest sets and counters are preserved.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$root = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
if (!(Test-Path -LiteralPath (Join-Path $repo 'SecureAgentLab.slnx'))) { throw 'Repository root required.' }
if (!(Test-Path -LiteralPath $root)) { return }
if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Artifact root must not be a link.' }
function Remove-GeneratedEntry([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup target escaped artifacts.' }
    $item = Get-Item -LiteralPath $full -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        if ($item.PSIsContainer) { [IO.Directory]::Delete($full) } else { [IO.File]::Delete($full) }
    } elseif ($item.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $full -Force) { Remove-GeneratedEntry $child.FullName }
        [IO.Directory]::Delete($full)
    } else {
        [IO.File]::SetAttributes($full, ($item.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly)))
        [IO.File]::Delete($full)
    }
}
$removed = 0
foreach ($case in 'comparisons','document-checks','document-demo','durable-checks','durable-demo','isolation','model-checks','phase7','phase8','phase8-isolated') {
    $directory = Join-Path $root $case
    if (!(Test-Path -LiteralPath $directory)) { continue }
    if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Case root must not be a link.' }
    foreach ($entry in Get-ChildItem -LiteralPath $directory -Force) {
        if ($entry.Name -match '^(?:[a-f0-9]{32}|(?:securelab|collab|compare)-[a-f0-9]{12}|suite-[a-f0-9]{32})$') {
            Remove-GeneratedEntry $entry.FullName
            $removed++
        }
    }
}
foreach ($entry in Get-ChildItem -LiteralPath $root -Force) {
    if ($entry.Name -match '^(?:gateway-[a-f0-9]{32}(?:\.err)?\.log|namespace-[a-z0-9-]+\.log|final-check-\d{8}-\d{6}|refactoring-baseline|namespace-verification|refactor-compat-v2)$') {
        Remove-GeneratedEntry $entry.FullName
        $removed++
    }
}
Write-Host "Removed $removed legacy generated artifact entries; latest sets, counters and unrecognized files preserved."
