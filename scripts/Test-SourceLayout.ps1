$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$failures = @()
$declaration = '(?m)^public\s+(?:(?:static|sealed|abstract|readonly|partial)\s+)*(?:class|interface|enum|struct|record(?:\s+(?:class|struct))?)\s+(\w+)'
foreach ($source in Get-ChildItem (Join-Path $repo src) -Recurse -Filter '*.cs' | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]') {
    $types = @([regex]::Matches((Get-Content -LiteralPath $source.FullName -Raw), $declaration))
    if ($types.Count -gt 1 -or ($types.Count -eq 1 -and $types[0].Groups[1].Value -ne $source.BaseName)) {
        $failures += $source.FullName
    }
}
# Top-level executable statements have no namespace; every declared type follows its project/folder.
foreach ($projectRoot in @('src', 'tests')) {
    foreach ($project in Get-ChildItem (Join-Path $repo $projectRoot) -Recurse -Filter '*.csproj') {
        foreach ($source in Get-ChildItem $project.Directory.FullName -Recurse -Filter '*.cs' | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]') {
            $relative = [IO.Path]::GetRelativePath($project.Directory.FullName, $source.Directory.FullName)
            $expected = $project.BaseName
            if ($relative -ne '.') { $expected += '.' + ($relative -replace '[\\/]', '.') }
            $content = Get-Content -LiteralPath $source.FullName -Raw
            $declared = [regex]::Match($content, '(?m)^namespace\s+([\w.]+)\s*;')
            if ($declared.Success) {
                if ($declared.Groups[1].Value -ne $expected) { $failures += "Namespace must be ${expected}: $($source.FullName)" }
            } elseif ($content -match '(?m)^(?:public|internal|file)\s+(?:(?:static|sealed|abstract|readonly|partial)\s+)*(?:class|interface|enum|struct|record|delegate)\s+') {
                $failures += "Missing namespace ${expected}: $($source.FullName)"
            }
        }
    }
}
foreach ($project in Get-ChildItem (Join-Path $repo src) -Recurse -Filter '*.csproj') {
    if ($project.BaseName -ne 'SecureAgentLab.Comparisons' -and (Get-Content -LiteralPath $project.FullName -Raw) -match 'ProjectReference[^>]*SecureAgentLab.Comparisons') {
        $failures += 'Normal project references deliberately vulnerable comparisons: ' + $project.FullName
    }
}
foreach ($script in Get-ChildItem $PSScriptRoot -Filter '*.ps1') {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    $failures += @($parseErrors | ForEach-Object Message)
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
Write-Host 'PASS folder namespaces, source type filenames, comparison dependency boundary and PowerShell syntax'
