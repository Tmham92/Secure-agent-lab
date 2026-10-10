# Dot-source this helper before changing lab environment variables.
function Set-LabProcessEnvironment {
    param([Parameter(Mandatory)][string]$Name, $Value)
    # PowerShell can coerce $null to an empty string when calling the .NET API.
    # New runtimes preserve empty strings, so removal must use the Env provider.
    if ($null -eq $Value) {
        Remove-Item -LiteralPath ('Env:' + $Name) -ErrorAction SilentlyContinue
    }
    else {
        [Environment]::SetEnvironmentVariable($Name, [string]$Value, 'Process')
    }
}
