# Requires PowerShell 7 and Docker Engine with Linux containers (iptables/ip6tables support).
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = 'securelab-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$compose = Join-Path $repo 'deploy/isolation/compose.yml'
$fixture = Join-Path $repo "artifacts/isolation/$project"
$names = @('LAB_ISOLATION_SIGNING_KEY','LAB_ISOLATION_FIXTURE','LAB_WORKER_CREDENTIAL')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
function Invoke-Compose {
    param([string[]]$Arguments, [string]$LogFile)
    if ($LogFile) {
        & docker compose -p $project -f $compose @Arguments 2>&1 | Tee-Object -FilePath $LogFile | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed: $($Arguments[0]); see $LogFile" }
        return
    }
    $result = & docker compose -p $project -f $compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed: $($Arguments[0])" }
    return $result
}
function Save-IsolationDiagnostics {
    # Never dump compose config, inspect environment or issuer output: those contain credentials.
    if (!(Test-Path -LiteralPath $fixture)) { return }
    $commands = @(
        @{ Name = 'services'; Args = @('ps','--all') },
        @{ Name = 'relay'; Args = @('logs','--no-color','--tail','150','relay') },
        @{ Name = 'relay-identity'; Args = @('exec','-T','relay','id') },
        @{ Name = 'relay-config-check'; Args = @('exec','-T','relay','nginx','-t') },
        @{ Name = 'ipv4-firewall'; Args = @('exec','-T','boundary','iptables','-nvL','OUTPUT','--line-numbers') },
        @{ Name = 'ipv6-firewall'; Args = @('exec','-T','boundary','ip6tables','-nvL','OUTPUT','--line-numbers') }
    )
    foreach ($command in $commands) {
        try {
            Write-Host "Isolation diagnostics: $($command.Name)"
            $arguments = $command.Args
            & docker compose -p $project -f $compose @arguments 2>&1 |
                Tee-Object -FilePath (Join-Path $fixture "diagnostics-$($command.Name).txt") | Out-Host
        }
        catch { Write-Warning "Could not collect $($command.Name) diagnostics." }
    }
}
try {
    & docker info --format '{{.OSType}}' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Start Docker Desktop in Linux container mode, then retry.' }
    $kind = & docker info --format '{{.OSType}}'
    if ($kind -ne 'linux') { throw 'This deployment requires Linux containers.' }
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'other-run.txt'), 'synthetic protected canary')
    & (Join-Path $PSScriptRoot 'New-DocumentFixtures.ps1') -Destination (Join-Path $fixture 'documents')
    $env:LAB_ISOLATION_FIXTURE = $fixture
    $env:LAB_ISOLATION_SIGNING_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:LAB_WORKER_CREDENTIAL = ''
    Invoke-Compose -Arguments @('build') | Out-Host
    Invoke-Compose -Arguments @('up','-d','boundary','gateway','relay') | Out-Host
    $run = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $output = & docker compose -p $project -f $compose exec -T gateway dotnet /operator/SecureAgentLab.Operator.dll isolation-run 2>$null
        if ($LASTEXITCODE -eq 0) { $run = ($output -join "`n") | ConvertFrom-Json; break }
        Start-Sleep -Seconds 1
    }
    if ($null -eq $run) { throw 'Gateway or positive controls did not become ready.' }
    Write-Host 'Positive controls passed: gateway IPv4/IPv6 and protected storage exist.'
    $env:LAB_WORKER_CREDENTIAL = $run.workerCredential
    Invoke-Compose -Arguments @('run','--no-deps','--name',"$project-readiness",'worker','--relay-readiness') -LogFile (Join-Path $fixture 'worker-readiness.log')
    # Two distinct containers demonstrate that scratch from the first run is unavailable in the second.
    foreach ($number in 1,2) {
        $workerName = "$project-worker-$number"
        Invoke-Compose -Arguments @('run','--no-deps','--name',$workerName,'worker','--isolation-checks') -LogFile (Join-Path $fixture "worker-$number.log")
        $details = (& docker inspect $workerName | ConvertFrom-Json)[0]
        if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect worker container.' }
        $hostConfig = $details.HostConfig
        if ($details.Config.User -ne '1654:1654' -or !$hostConfig.ReadonlyRootfs -or
            $hostConfig.CapDrop -notcontains 'ALL' -or $hostConfig.CapAdd.Count -ne 0 -or
            $hostConfig.Privileged -or $hostConfig.Memory -ne 134217728 -or
            $hostConfig.NanoCpus -ne 500000000 -or $hostConfig.PidsLimit -ne 32 -or
            $hostConfig.SecurityOpt -notcontains 'no-new-privileges:true' -or
            @($details.Mounts | Where-Object { $_.Type -ne 'tmpfs' }).Count -ne 0) {
            throw 'Worker deployment privileges, limits or mounts differ from the expected boundary.'
        }
        Write-Host "PASS run $number deployment inspection"
    }
    $effects = ((Invoke-Compose -Arguments @('exec','-T','gateway','dotnet','/operator/SecureAgentLab.Operator.dll','isolation-effects')) -join "`n") | ConvertFrom-Json
    if ($effects.documentReads -ne 2 -or $effects.publications -ne 0 -or
        [IO.File]::ReadAllText((Join-Path $fixture 'other-run.txt')) -ne 'synthetic protected canary') {
        throw 'Unexpected tool effects or protected storage modification.'
    }
    Write-Host 'Mock effects: 2 reads, 0 publications; protected canary unchanged.'
    Write-Host 'Isolation demo complete. Both workers passed bypass checks and authorized proposals.'
}
catch {
    Save-IsolationDiagnostics
    throw
}
finally {
    # No compose config/inspect output is printed: gateway configuration contains its ephemeral signing key.
    & docker compose -p $project -f $compose down --volumes --remove-orphans 2>$null | Out-Null
    foreach ($number in 1,2) { & docker rm -f "$project-worker-$number" 2>$null | Out-Null }
    & docker rm -f "$project-readiness" 2>$null | Out-Null
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
