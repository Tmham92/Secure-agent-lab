[CmdletBinding()]
param([ValidateSet('both','isolation','containment')][string]$Scenario = 'both')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = 'compare-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$compose = Join-Path $repo 'deploy/comparisons/compose.yml'
$evidence = Join-Path $repo "artifacts/comparisons/$project"
$savedEvidence = $env:COMPARISON_EVIDENCE; $savedSecret = $env:COMPARISON_FAKE_SECRET
$watch = [Diagnostics.Stopwatch]::StartNew()
function Compose([string[]]$Arguments) {
    $result = & docker compose -p $project -f $compose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Container comparison failed: $($Arguments[0])" }
    return $result
}
function Exec([string]$Service,[string]$Mode) {
    Compose @('exec','-T',$Service,'dotnet','SecureAgentLab.Comparisons.dll','--container',$Mode) | Out-Host
}
function Copy-Evidence([string]$Service,[string]$File) {
    $target = Join-Path $evidence "$Service-$File"
    # Docker archive/cp does not expose these private tmpfs files; read through the live owned container.
    $json = Compose @('exec','-T',$Service,'cat',"/workspace/$File")
    $json | Set-Content -LiteralPath $target
    return Get-Content -LiteralPath $target -Raw | ConvertFrom-Json
}
try {
    if ((& docker info --format '{{.OSType}}') -ne 'linux') { throw 'Start Docker in Linux container mode.' }
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    $env:COMPARISON_EVIDENCE = $evidence
    $env:COMPARISON_FAKE_SECRET = Join-Path $evidence 'generated-fake-secret.txt'
    [IO.File]::WriteAllText($env:COMPARISON_FAKE_SECRET,'FAKE_SECRET_COMPARISON_ONLY')
    Compose @('build') | Out-Host
    Compose @('up','-d','boundary','capture','relay','unsafe','secure') | Out-Host
    $network = (& docker network inspect "${project}_sealed" | ConvertFrom-Json)[0]
    if ($LASTEXITCODE -ne 0 -or !$network.Internal) { throw 'Outer network is not internal.' }
    # Readiness is an actual permitted read from both worker origins, not just a running service flag.
    foreach ($variant in @('unsafe','secure')) {
        $ready = $false
        for ($n=0; $n -lt 30; $n++) {
            $id = (Compose @('ps','-q',$variant)) -join ''
            $details = (& docker inspect $id | ConvertFrom-Json)[0]
            $expectedUser = if ($variant -eq 'unsafe') { '1654:1654' } else { '1657:1657' }
            $binds = @($details.Mounts | Where-Object { $_.Type -ne 'tmpfs' })
            if ($details.Config.User -ne $expectedUser -or !$details.HostConfig.ReadonlyRootfs -or $details.HostConfig.Privileged -or
                $details.HostConfig.CapDrop -notcontains 'ALL' -or $details.HostConfig.CapAdd.Count -ne 0 -or
                $details.HostConfig.SecurityOpt -notcontains 'no-new-privileges:true' -or $details.HostConfig.Memory -ne 201326592 -or
                $details.HostConfig.NanoCpus -ne 500000000 -or $details.HostConfig.PidsLimit -ne 64 -or $details.HostConfig.PidMode -eq 'host' -or
                ($variant -eq 'secure' -and $binds.Count -ne 0) -or
                ($variant -eq 'unsafe' -and ($binds.Count -ne 1 -or $binds[0].Destination -ne '/fake/secret.txt' -or $binds[0].RW))) { throw 'Unexpected outer container boundary.' }
            # wget is intentionally absent; the application readiness mode checks the fixed relay read.
            & docker compose -p $project -f $compose exec -T $variant dotnet SecureAgentLab.Comparisons.dll --container readiness 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            Start-Sleep -Seconds 1
        }
        if (!$ready) { throw 'Worker-origin authorized relay read never became ready.' }
        [ordered]@{User=$details.Config.User;ReadOnly=$details.HostConfig.ReadonlyRootfs;CapDrop=$details.HostConfig.CapDrop;Memory=$details.HostConfig.Memory;Cpu=$details.HostConfig.NanoCpus;Pids=$details.HostConfig.PidsLimit;BindMounts=$binds.Count} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "$variant-deployment.json")
    }
    $results = @()
    if ($Scenario -in @('both','isolation')) {
        Exec 'unsafe' 'isolation-unsafe'; Exec 'secure' 'isolation-secure'; Exec 'capture' 'inspect'
        Compose @('exec','-T','capture','cat','/tmp/capture-evidence.json') | Set-Content -LiteralPath (Join-Path $evidence 'capture.json')
        $u = Copy-Evidence 'unsafe' 'isolation.json'; $s = Copy-Evidence 'secure' 'isolation.json'
        $capture = Get-Content -LiteralPath (Join-Path $evidence 'capture.json') -Raw | ConvertFrom-Json
        if (!$u.Transferred -or !$u.FakeMountPresent -or $s.Transferred -or $s.FakeMountPresent -or !$u.PositiveRead -or !$s.PositiveRead -or
            @($capture.unsafe).Count -ne 1 -or @($capture.secure).Count -ne 0) { throw 'Isolation effects mismatch.' }
        $results += [ordered]@{Scenario='5-isolation';MissingSafeguard='Private mounts and worker egress';Input='Read fake mount and POST only to disposable local capture';Unsafe=$u;Secure=$s;Passed=$true;ElapsedMilliseconds=$watch.ElapsedMilliseconds}
        Write-Host 'PASS 5-isolation: actual fake-secret transfer=1 unsafe, 0 secure; permitted reads passed.'
    }
    if ($Scenario -in @('both','containment')) {
        Exec 'unsafe' 'containment-unsafe'; Exec 'secure' 'containment-secure'
        $u = Copy-Evidence 'unsafe' 'containment.json'; $s = Copy-Evidence 'secure' 'containment.json'
        if (!$u.LateWrite -or $s.LateWrite -or !$u.ChildExited -or !$s.ChildExited -or !$s.Revoked -or !$s.KillSignal -or
            !$u.EarlierWritePreserved -or !$s.EarlierWritePreserved -or $u.LateAt -lt $u.StopAck) { throw 'Containment effects mismatch.' }
        $results += [ordered]@{Scenario='10-containment';MissingSafeguard='Active revocation and owned-process termination';Input='Earlier commit, controlled child, stop acknowledgement then release late action';Unsafe=$u;Secure=$s;Passed=$true;ElapsedMilliseconds=$watch.ElapsedMilliseconds}
        Write-Host 'PASS 10-containment: late write=1 unsafe, 0 secure; earlier effects preserved; both children exited.'
    }
    foreach ($result in $results) {
        $result.InputSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($result.Input)))
        $result.Expected = 'Unsafe failure observed; secure failure absent; authorized positive controls pass'
    }
    $results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence 'comparisons.json')
    @('# Container comparison results','','| Scenario | Unsafe | Secure | Result |','|---|---|---|---|') +
        @($results | ForEach-Object { "| $($_.Scenario) | failure observed | failure absent, allowed control passed | PASS |" }) |
        Set-Content -LiteralPath (Join-Path $evidence 'summary.md')
    Write-Host "Container comparisons passed. Evidence: $evidence"
    [pscustomobject]@{ComparisonEvidence=$evidence;PairCount=$results.Count}
}
finally {
    & docker compose -p $project -f $compose down --volumes --remove-orphans 2>$null | Out-Null
    $cleanup = $LASTEXITCODE
    $env:COMPARISON_EVIDENCE = $savedEvidence; $env:COMPARISON_FAKE_SECRET = $savedSecret
    if ($cleanup -ne 0) { throw "Cleanup failed for owned project $project" }
}
