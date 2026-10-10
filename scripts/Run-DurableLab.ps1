param([int]$GatewayPort = 5188, [int]$AuditPort = 5189)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProcessEnvironment.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
$artifactRun = Start-LabArtifactRun -Repository $repoRoot -Case 'durable-demo'
try {
    $gatewayDll = Join-Path $repoRoot 'src/SecureAgentLab.Api/bin/Release/net10.0/SecureAgentLab.Api.dll'
    $auditDll = Join-Path $repoRoot 'src/SecureAgentLab.AuditCollector/bin/Release/net10.0/SecureAgentLab.AuditCollector.dll'
    $operatorDll = Join-Path $repoRoot 'src/SecureAgentLab.Operator/bin/Release/net10.0/SecureAgentLab.Operator.dll'
    $workerDll = Join-Path $repoRoot 'src/SecureAgentLab.Worker/bin/Release/net10.0/SecureAgentLab.Worker.dll'
    foreach ($dll in @($gatewayDll,$auditDll,$operatorDll,$workerDll)) {
        if (!(Test-Path -LiteralPath $dll)) { throw 'Build the Release solution first.' }
    }
    if ($GatewayPort -eq $AuditPort -or $GatewayPort -lt 1024 -or $AuditPort -lt 1024 -or $GatewayPort -gt 65535 -or $AuditPort -gt 65535) {
        throw 'Use two distinct local ports between 1024 and 65535.'
    }
    $runRoot = $artifactRun.DirectoryPath
    [System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
    $generatedNames = @('Audit__LogDirectory','Audit__CheckpointDirectory','Audit__StreamId','Audit__SigningPrivateKey',
        'Audit__WriteKey','Audit__AllowLoopbackHttp','Lab__Issuer','Lab__Audience','Lab__SigningKey','Lab__UseInMemory',
        'Lab__AllowLoopbackHttp','Lab__StateDirectory','Lab__AuditUrl','Lab__AuditWriteKey','Lab__AuditPublicKey',
        'Lab__AuditStreamId','Lab__PolicyVersion','LAB_WORKER_CREDENTIAL','LAB_GATEWAY_URL','LAB_ALLOW_LOOPBACK_HTTP')
    $existingNames = @(Get-ChildItem Env: | Where-Object { $_.Name -match '^(Lab__|Audit__|LAB_)' } | ForEach-Object Name)
    $names = @($generatedNames + $existingNames | Select-Object -Unique)
    $previous = @{}
    foreach ($name in $names) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
        Set-LabProcessEnvironment -Name $name -Value $null
    }
    $gatewayProcess = $null
    $auditProcess = $null
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    function Start-LabProcess($dll,$port,$label) {
        Start-Process dotnet -ArgumentList @(('"' + $dll + '"'),'--urls',("http://127.0.0.1:$port")) `
            -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $runRoot "$label.log") `
            -RedirectStandardError (Join-Path $runRoot "$label.err.log")
    }
    function Wait-Lab($url,$headers,$process) {
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            if ($process.HasExited) { throw 'A lab process exited; inspect artifacts logs.' }
            try { return Invoke-RestMethod -Uri $url -Headers $headers } catch { Start-Sleep -Milliseconds 200 }
        }
        throw 'Lab service did not become ready.'
    }
    try {
        $streamId = [guid]::NewGuid().ToString('N')
        $writerKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $hostKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $publicKey = $rsa.ExportSubjectPublicKeyInfoPem()
        [System.IO.File]::WriteAllText((Join-Path $runRoot 'audit-public.pem'), $publicKey)
        $env:Audit__LogDirectory = Join-Path $runRoot 'collector-log'
        $env:Audit__CheckpointDirectory = Join-Path $runRoot 'independent-checkpoint'
        $env:Audit__StreamId = $streamId
        $env:Audit__SigningPrivateKey = $rsa.ExportPkcs8PrivateKeyPem()
        $env:Audit__WriteKey = $writerKey
        $env:Audit__AllowLoopbackHttp = 'true'
        $auditProcess = Start-LabProcess $auditDll $AuditPort 'collector'
        $auditUrl = "http://127.0.0.1:$AuditPort"
        Wait-Lab "$auditUrl/checkpoint" @{ Authorization = "Bearer $writerKey" } $auditProcess | Out-Null
        # The collector signing private key never enters the gateway or worker environment.
        foreach ($name in $names | Where-Object { $_ -like 'Audit__*' }) { Set-LabProcessEnvironment -Name $name -Value $null }
        $hostConfiguration = @{
            Lab__Issuer = 'durable-lab-issuer'; Lab__Audience = 'durable-lab-gateway'; Lab__SigningKey = $hostKey
            Lab__UseInMemory = 'false'; Lab__AllowLoopbackHttp = 'true'; Lab__PolicyVersion = 'synthetic-v1'
            Lab__StateDirectory = (Join-Path $runRoot 'gateway-state'); Lab__AuditUrl = $auditUrl; Lab__AuditWriteKey = $writerKey
            Lab__AuditPublicKey = $publicKey; Lab__AuditStreamId = $streamId
        }
        foreach ($name in $hostConfiguration.Keys) { Set-LabProcessEnvironment -Name $name -Value $hostConfiguration[$name] }
        $gatewayUrl = "http://127.0.0.1:$GatewayPort"
        $gatewayProcess = Start-LabProcess $gatewayDll $GatewayPort 'gateway'
        $operatorCredential = (& dotnet $operatorDll issue-operator).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Operator issuance failed.' }
        $operatorHeaders = @{ Authorization = "Bearer $operatorCredential" }
        Wait-Lab "$gatewayUrl/operator/effects" $operatorHeaders $gatewayProcess | Out-Null
        $run = Invoke-RestMethod -Method Post -Uri "$gatewayUrl/operator/runs" -Headers $operatorHeaders `
            -ContentType 'application/json' -Body '{"calls":30,"responseBytes":4096,"lifetimeSeconds":180}'
        $proposal = @{ operation = 1; resource = 'reports/draft'; estimatedBytes = 0; content = 'Synthetic phase 3 reviewed draft.' }
        $approvalBody = @{ proposal = $proposal; lifetimeSeconds = 120 } | ConvertTo-Json -Depth 5 -Compress
        $preview = Invoke-RestMethod -Method Post -Uri "$gatewayUrl/operator/runs/$($run.runId)/approval-preview" `
            -Headers $operatorHeaders -ContentType 'application/json' -Body $approvalBody
        Write-Output "Operator review: $($preview.destination) — $($preview.content)"
        Write-Output "Content hash: $($preview.contentHash)"
        $approval = Invoke-RestMethod -Method Post -Uri "$gatewayUrl/operator/runs/$($run.runId)/approvals" `
            -Headers $operatorHeaders -ContentType 'application/json' -Body $approvalBody
        foreach ($name in $names | Where-Object { $_ -match '^(Lab__|Audit__)' }) { Set-LabProcessEnvironment -Name $name -Value $null }
        $env:LAB_WORKER_CREDENTIAL = $run.workerCredential
        $env:LAB_GATEWAY_URL = $gatewayUrl
        $env:LAB_ALLOW_LOOPBACK_HTTP = 'true'
        & dotnet $workerDll
        if ($LASTEXITCODE -ne 0) { throw 'Worker walkthrough failed.' }
        foreach ($name in @('LAB_WORKER_CREDENTIAL','LAB_GATEWAY_URL','LAB_ALLOW_LOOPBACK_HTTP')) { Set-LabProcessEnvironment -Name $name -Value $null }
        Stop-Process -Id $gatewayProcess.Id
        $gatewayProcess.WaitForExit()
        foreach ($name in $hostConfiguration.Keys) { Set-LabProcessEnvironment -Name $name -Value $hostConfiguration[$name] }
        $gatewayProcess = Start-LabProcess $gatewayDll $GatewayPort 'gateway-restarted'
        Wait-Lab "$gatewayUrl/operator/effects" $operatorHeaders $gatewayProcess | Out-Null
        $workerHeaders = @{ Authorization = "Bearer $($run.workerCredential)" }
        $publicationBody = @{ proposal = $proposal; approvalTicket = $approval.ticket; idempotencyKey = 'demo-publication-once' } | ConvertTo-Json -Depth 5 -Compress
        $result = Invoke-RestMethod -Method Post -Uri "$gatewayUrl/worker/proposals" -Headers $workerHeaders `
            -ContentType 'application/json' -Body $publicationBody
        Write-Output "Publication after gateway restart: $($result.reason)"
        $retry = Invoke-RestMethod -Method Post -Uri "$gatewayUrl/worker/proposals" -Headers $workerHeaders `
            -ContentType 'application/json' -Body $publicationBody
        Write-Output "Retry with same idempotency key: $($retry.reason)"
        $effects = Invoke-RestMethod -Uri "$gatewayUrl/operator/effects" -Headers $operatorHeaders
        if ($effects.publications -ne 1) { throw 'Unexpected publication count.' }
        Write-Output "Durable mock effects: $($effects.documentReads) reads, $($effects.publications) publication"
        Invoke-RestMethod -Method Post -Uri "$gatewayUrl/operator/stop" -Headers $operatorHeaders | Out-Null
    } finally {
        foreach ($process in @($gatewayProcess,$auditProcess)) {
            if ($null -ne $process -and !$process.HasExited) { Stop-Process -Id $process.Id }
        }
        $rsa.Dispose()
        foreach ($name in $names) { Set-LabProcessEnvironment -Name $name -Value $previous[$name] }
    }

} finally { $artifactRun.Dispose() }
