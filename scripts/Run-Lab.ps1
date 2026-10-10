param([int]$Port = 5188, [string]$DocumentRoot, [string]$ModelProposalsFile, [int]$ExpectedDocumentReads = -1)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ProcessEnvironment.ps1')
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'ArtifactRuns.ps1')
$artifactRun = Start-LabArtifactRun -Repository $repoRoot -Case $(if ($ModelProposalsFile) { 'model-gateway' } elseif ($DocumentRoot) { 'document-gateway' } else { 'gateway' })
try {
    $gatewayDll = Join-Path $repoRoot 'src/SecureAgentLab.Api/bin/Release/net10.0/SecureAgentLab.Api.dll'
    $workerDll = Join-Path $repoRoot 'src/SecureAgentLab.Worker/bin/Release/net10.0/SecureAgentLab.Worker.dll'
    $operatorDll = Join-Path $repoRoot 'src/SecureAgentLab.Operator/bin/Release/net10.0/SecureAgentLab.Operator.dll'
    foreach ($dll in @($gatewayDll,$workerDll,$operatorDll)) {
        if (!(Test-Path -LiteralPath $dll)) { throw 'Build the Release solution first.' }
    }
    if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Choose a local port between 1024 and 65535.' }
    $logDirectory = $artifactRun.DirectoryPath
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null

    $environmentNames = @('Lab__UseInMemory','Lab__Issuer','Lab__Audience','Lab__SigningKey','Lab__AllowLoopbackHttp','Lab__DocumentRoot','Lab__PolicyVersion',
        'LAB_GATEWAY_URL','LAB_WORKER_CREDENTIAL','LAB_ALLOW_LOOPBACK_HTTP','LAB_MODEL_PROPOSALS_FILE','OPENAI_API_KEY')
    $environmentNames = @($environmentNames + @(Get-ChildItem Env: | Where-Object { $_.Name -match '^(Lab__|Audit__|LAB_)' } | ForEach-Object Name) | Select-Object -Unique)
    $previousEnvironment = @{}
    foreach ($name in $environmentNames) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        Set-LabProcessEnvironment -Name $name -Value $null
    }
    $gatewayLog = Join-Path $logDirectory 'gateway.log'
    $gatewayErrorLog = Join-Path $logDirectory 'gateway.err.log'
    $process = $null
    try {
        $env:Lab__UseInMemory = 'true'
        $env:Lab__PolicyVersion = 'synthetic-v1'
        $env:Lab__Issuer = 'local-lab-issuer'
        $env:Lab__Audience = 'local-lab-gateway'
        $env:Lab__SigningKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $env:Lab__AllowLoopbackHttp = 'true'
        if ($DocumentRoot) {
            if (![IO.Path]::IsPathFullyQualified($DocumentRoot)) { throw 'Use an absolute document root.' }
            $env:Lab__DocumentRoot = $DocumentRoot
        }
        # The process receives the host key at launch. The worker below does not.
        $process = Start-Process dotnet -ArgumentList @(('"' + $gatewayDll + '"'),'--urls',("http://127.0.0.1:$Port")) -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $gatewayLog `
            -RedirectStandardError $gatewayErrorLog
        $operatorCredential = (& dotnet $operatorDll issue-operator).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Operator credential issuance failed.' }
        $headers = @{ Authorization = "Bearer $operatorCredential" }
        $url = "http://127.0.0.1:$Port"
        $run = $null
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            if ($process.HasExited) { throw "Gateway exited. Inspect $gatewayLog and $gatewayErrorLog." }
            try {
                $run = Invoke-RestMethod -NoProxy -TimeoutSec 2 -Method Post -Uri "$url/operator/runs" -Headers $headers `
                    -ContentType 'application/json' -Body '{"calls":20,"responseBytes":4096,"lifetimeSeconds":180}'
                break
            } catch {
                $status = if ($null -ne $_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { $null }
                if ($null -ne $status -and $status -ne 503) {
                    $reason = 'unknown_rejection'
                    try {
                        $errorCode = ($_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction Stop).error
                        if ($errorCode -in @('invalid_run_limits', 'invalid_run_grant')) { $reason = $errorCode }
                    } catch { }
                    throw "Gateway responded with HTTP $status ($reason) while creating a run; this is not a startup timeout. Inspect $gatewayLog and $gatewayErrorLog."
                }
                Start-Sleep -Milliseconds 200
            }
        }
        if ($null -eq $run) { throw "Gateway did not become ready at $url. Inspect $gatewayLog and $gatewayErrorLog." }
        $env:LAB_WORKER_CREDENTIAL = $run.workerCredential
        $env:LAB_GATEWAY_URL = $url
        $env:LAB_ALLOW_LOOPBACK_HTTP = 'true'
        if ($ModelProposalsFile) { $env:LAB_MODEL_PROPOSALS_FILE = $ModelProposalsFile }
        foreach ($name in $environmentNames | Where-Object { $_ -match '^(Lab__|Audit__)' }) {
            Set-LabProcessEnvironment -Name $name -Value $null
        }
        & dotnet $workerDll
        if ($LASTEXITCODE -ne 0) { throw 'Worker walkthrough failed.' }
        $effects = Invoke-RestMethod -NoProxy -TimeoutSec 5 -Uri "$url/operator/effects" -Headers $headers
        if ($effects.publications -ne 0 -or ($ExpectedDocumentReads -ge 0 -and $effects.documentReads -ne $ExpectedDocumentReads)) {
            throw 'Unexpected tool effects in the unapproved worker scenario.'
        }
        Write-Output "Mock effects: $($effects.documentReads) reads, $($effects.publications) publications"
        Invoke-RestMethod -NoProxy -TimeoutSec 5 -Method Post -Uri "$url/operator/stop" -Headers $headers | Out-Null
    } finally {
        if ($null -ne $process -and !$process.HasExited) { Stop-Process -Id $process.Id }
        foreach ($name in $environmentNames) { Set-LabProcessEnvironment -Name $name -Value $previousEnvironment[$name] }
    }

} finally { $artifactRun.Dispose() }
