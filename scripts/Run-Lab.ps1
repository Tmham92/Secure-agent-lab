param([int]$Port = 5188, [string]$DocumentRoot, [string]$ModelProposalsFile, [int]$ExpectedDocumentReads = -1)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$gatewayDll = Join-Path $repoRoot 'src/SecureAgentLab.Api/bin/Release/net10.0/SecureAgentLab.Api.dll'
$workerDll = Join-Path $repoRoot 'src/SecureAgentLab.Worker/bin/Release/net10.0/SecureAgentLab.Worker.dll'
$operatorDll = Join-Path $repoRoot 'src/SecureAgentLab.Operator/bin/Release/net10.0/SecureAgentLab.Operator.dll'
foreach ($dll in @($gatewayDll,$workerDll,$operatorDll)) {
    if (!(Test-Path -LiteralPath $dll)) { throw 'Build the Release solution first.' }
}
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Choose a local port between 1024 and 65535.' }
$logDirectory = Join-Path $repoRoot 'artifacts'
[System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$logSuffix = [guid]::NewGuid().ToString('N')
$environmentNames = @('Lab__UseInMemory','Lab__Issuer','Lab__Audience','Lab__SigningKey','Lab__AllowLoopbackHttp','Lab__DocumentRoot',
    'LAB_GATEWAY_URL','LAB_WORKER_CREDENTIAL','LAB_ALLOW_LOOPBACK_HTTP','LAB_MODEL_PROPOSALS_FILE','OPENAI_API_KEY')
$environmentNames = @($environmentNames + @(Get-ChildItem Env: | Where-Object { $_.Name -match '^(Lab__|Audit__|LAB_)' } | ForEach-Object Name) | Select-Object -Unique)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $null, 'Process')
}
$process = $null
try {
    $env:Lab__UseInMemory = 'true'
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
        -RedirectStandardOutput (Join-Path $logDirectory "gateway-$logSuffix.log") `
        -RedirectStandardError (Join-Path $logDirectory "gateway-$logSuffix.err.log")
    $operatorCredential = (& dotnet $operatorDll issue-operator).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Operator credential issuance failed.' }
    $headers = @{ Authorization = "Bearer $operatorCredential" }
    $url = "http://127.0.0.1:$Port"
    $run = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($process.HasExited) { throw 'Gateway exited; inspect artifacts logs.' }
        try {
            $run = Invoke-RestMethod -Method Post -Uri "$url/operator/runs" -Headers $headers `
                -ContentType 'application/json' -Body '{"calls":20,"responseBytes":4096,"lifetimeSeconds":180}'
            break
        } catch { Start-Sleep -Milliseconds 200 }
    }
    if ($null -eq $run) { throw 'Gateway did not become ready.' }
    $env:LAB_WORKER_CREDENTIAL = $run.workerCredential
    $env:LAB_GATEWAY_URL = $url
    $env:LAB_ALLOW_LOOPBACK_HTTP = 'true'
    if ($ModelProposalsFile) { $env:LAB_MODEL_PROPOSALS_FILE = $ModelProposalsFile }
    foreach ($name in $environmentNames | Where-Object { $_ -match '^(Lab__|Audit__)' }) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    & dotnet $workerDll
    if ($LASTEXITCODE -ne 0) { throw 'Worker walkthrough failed.' }
    $effects = Invoke-RestMethod -Uri "$url/operator/effects" -Headers $headers
    if ($effects.publications -ne 0 -or ($ExpectedDocumentReads -ge 0 -and $effects.documentReads -ne $ExpectedDocumentReads)) {
        throw 'Unexpected tool effects in the unapproved worker scenario.'
    }
    Write-Output "Mock effects: $($effects.documentReads) reads, $($effects.publications) publications"
    Invoke-RestMethod -Method Post -Uri "$url/operator/stop" -Headers $headers | Out-Null
} finally {
    if ($null -ne $process -and !$process.HasExited) { Stop-Process -Id $process.Id }
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name,$previousEnvironment[$name],'Process') }
}
