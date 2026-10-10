$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProcessEnvironment.ps1')
$name = 'LAB_ENVIRONMENT_CHECK_' + [Guid]::NewGuid().ToString('N')
try {
    Set-LabProcessEnvironment -Name $name -Value 'temporary'
    Set-LabProcessEnvironment -Name $name -Value $null
    if (Test-Path -LiteralPath ('Env:' + $name)) { throw 'Removed variable still exists.' }
    # Exercise exactly the snapshot/restore pattern used by the launchers.
    $saved = [Environment]::GetEnvironmentVariable($name, 'Process')
    Set-LabProcessEnvironment -Name $name -Value 'temporary'
    Set-LabProcessEnvironment -Name $name -Value $saved
    if (Test-Path -LiteralPath ('Env:' + $name)) { throw 'Restoring absence created an empty variable.' }
    Set-LabProcessEnvironment -Name $name -Value ''
    if (!(Test-Path -LiteralPath ('Env:' + $name))) { throw 'Intentional empty value was not preserved.' }
    $saved = [Environment]::GetEnvironmentVariable($name, 'Process')
    Set-LabProcessEnvironment -Name $name -Value 'temporary'
    Set-LabProcessEnvironment -Name $name -Value $saved
    if (!(Test-Path -LiteralPath ('Env:' + $name)) -or [Environment]::GetEnvironmentVariable($name) -ne '') { throw 'Empty restoration failed.' }
    Set-LabProcessEnvironment -Name $name -Value 'original'
    $saved = [Environment]::GetEnvironmentVariable($name, 'Process')
    Set-LabProcessEnvironment -Name $name -Value $null
    Set-LabProcessEnvironment -Name $name -Value $saved
    if ([Environment]::GetEnvironmentVariable($name) -ne 'original') { throw 'Nonempty restoration failed.' }
    Write-Host 'PASS environment removal and absent/empty/nonempty restoration'
}
finally { Set-LabProcessEnvironment -Name $name -Value $null }
