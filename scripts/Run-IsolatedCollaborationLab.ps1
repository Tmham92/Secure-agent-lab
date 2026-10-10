$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProcessEnvironment.ps1')
$repo = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
$artifactRun = Start-LabArtifactRun -Repository $repo -Case 'phase8-isolated'
try {
    $project = 'collab-' + [Guid]::NewGuid().ToString('N').Substring(0,12)
    $compose = Join-Path $repo 'deploy/collaboration/compose.yml'
    $evidence = $artifactRun.DirectoryPath
    $names = @('COLLAB_BROKER_KEY','COLLAB_EVAL_KEY','COLLAB_PRIVATE_KEY','COLLAB_PUBLIC_KEY','COLLAB_EVIDENCE','COLLAB_RESEARCHER_TOKEN','COLLAB_WRITER_TOKEN','COLLAB_EVAL_OPERATOR')
    $saved = @{}; foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
    function Compose([string[]]$Arguments) {
        $result = & docker compose -p $project -f $compose @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Collaboration Compose failed: $($Arguments[0])" }
        return $result
    }
    try {
        if ((& docker info --format '{{.OSType}}') -ne 'linux') { throw 'Start Docker in Linux container mode.' }
        New-Item -ItemType Directory -Path $evidence -Force | Out-Null
        $env:COLLAB_EVIDENCE = $evidence
        $env:COLLAB_BROKER_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $env:COLLAB_EVAL_KEY = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $rsa = [Security.Cryptography.RSA]::Create(2048)
        try { $env:COLLAB_PRIVATE_KEY = $rsa.ExportRSAPrivateKeyPem(); $env:COLLAB_PUBLIC_KEY = $rsa.ExportSubjectPublicKeyInfoPem() } finally { $rsa.Dispose() }
        Compose @('build') | Out-Host
        Compose @('up','-d','boundary','broker','evaluator','relay') | Out-Host
        $issued = $null
        for ($n=0; $n -lt 30; $n++) {
            $output = & docker compose -p $project -f $compose exec -T broker dotnet SecureAgentLab.Collaboration.dll --container-supervisor issue 2>$null
            if ($LASTEXITCODE -eq 0) { $issued = ($output -join "`n") | ConvertFrom-Json; break }
            Start-Sleep -Seconds 1
        }
        if ($null -eq $issued) { throw 'Broker readiness/grant issuance failed.' }
        $env:COLLAB_RESEARCHER_TOKEN = $issued.Researcher; $env:COLLAB_WRITER_TOKEN = $issued.Writer
        $env:COLLAB_EVAL_OPERATOR = (Compose @('exec','-T','evaluator','dotnet','SecureAgentLab.Collaboration.dll','--container-supervisor','eval-token')) -join ''
        Compose @('up','-d','researcher','writer') | Out-Host
        foreach ($actor in @('researcher','writer')) {
            Compose @('exec','-T',$actor,'dotnet','SecureAgentLab.Collaboration.dll','--container-agent','probes') | Tee-Object -FilePath (Join-Path $evidence "$actor-probes.log") | Out-Host
            $id = (Compose @('ps','-q',$actor)) -join ''
            $details = (& docker inspect $id | ConvertFrom-Json)[0]
            if ($LASTEXITCODE -ne 0 -or !$details.HostConfig.ReadonlyRootfs -or $details.HostConfig.Privileged -or
                $details.Config.User -ne '1654:1654' -or $details.HostConfig.CapDrop -notcontains 'ALL' -or $details.HostConfig.CapAdd.Count -ne 0 -or
                $details.HostConfig.SecurityOpt -notcontains 'no-new-privileges:true' -or $details.HostConfig.Memory -ne 201326592 -or
                $details.HostConfig.NanoCpus -ne 500000000 -or $details.HostConfig.PidsLimit -ne 64 -or $details.HostConfig.PidMode -eq 'host' -or
                @($details.Mounts | Where-Object { $_.Type -ne 'tmpfs' }).Count -ne 0) { throw 'Unexpected worker deployment.' }
            [ordered]@{User=$details.Config.User;ReadOnly=$details.HostConfig.ReadonlyRootfs;CapDrop=$details.HostConfig.CapDrop;Memory=$details.HostConfig.Memory;Cpu=$details.HostConfig.NanoCpus;Pids=$details.HostConfig.PidsLimit;PrivateTmpfsOnly=$true} |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "$actor-deployment.json")
        }
        foreach ($step in @(@('researcher','facts'),@('writer','combine'),@('researcher','ack'),@('writer','submit'))) {
            Compose @('exec','-T',$step[0],'dotnet','SecureAgentLab.Collaboration.dll','--container-agent',$step[1]) | Out-Host
        }
        Compose @('exec','-T','-e','COLLAB_EVAL_OPERATOR','broker','dotnet','SecureAgentLab.Collaboration.dll','--container-supervisor','evaluate') | Out-Host
        foreach ($file in @('signed-transcript.json','evaluation.json','broker-public-key.pem')) {
            Compose @('exec','-T','broker','cat',"/tmp/evidence/$file") | Set-Content -LiteralPath (Join-Path $evidence $file)
        }
        Write-Host "Isolated collaboration complete. Evidence: $evidence"
    }
    finally {
        & docker compose -p $project -f $compose down --volumes --remove-orphans 2>$null | Out-Null
        $cleanup = $LASTEXITCODE
        foreach ($name in $names) { Set-LabProcessEnvironment -Name $name -Value $saved[$name] }
        if ($cleanup -ne 0) { throw "Cleanup failed for owned project $project" }
    }

} finally { $artifactRun.Dispose() }
