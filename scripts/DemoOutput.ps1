function Write-LabCodeFlow {

    param([Parameter(Mandatory)][string]$Case)
    $flow = switch ($Case) {
        'document-demo' { @'
Run-DocumentLab.ps1 -> New-DocumentFixtures.ps1: create pinned synthetic documents
  -> Run-Lab.ps1 -DocumentRoot -> Worker/Program.cs: propose document IDs
  -> Api/Hosting/GatewayServices.cs: select Core/Documents/FileDocumentReader.cs
  -> Core/Execution/Gateway.cs + Policy/DefaultPolicy.cs: authorize
  -> Core/Documents/DocumentExecutor.cs: read permitted content
  -> Verify effects; stop gateway; retain fixture and gateway evidence guides
Boundary: the host owns the document root; the worker proposes scoped IDs, not arbitrary paths.
'@ }
        'model-demo' { @'
Run-ModelLab.ps1 -> offline fixtures/model/adversarial-proposals.json by default
  -> Only with -Live: ModelHost/Hosting/ModelHostApplication.cs
       -> ModelHost/Clients/ResponsesProposalClient.cs: request and validate model output
  -> Run-Lab.ps1 -ModelProposalsFile -> Core/Proposals/ModelProposalSource.cs
  -> Worker -> HTTP /worker/proposals -> Gateway.cs + DefaultPolicy.cs
  -> Execute permitted work; verify effects; retain model/gateway evidence guides
Boundary: model output is untrusted and grants no permissions. The API key stays out of the worker.
'@ }
        'durable-demo' { @'
Run-DurableLab.ps1 -> AuditCollector/Program.cs: separate signed audit collector
  -> Api/Hosting/GatewayServices.cs: select Durable/Execution/DurableGateway.cs
  -> Operator: review exact publication content and issue a bound approval
  -> Worker -> HTTP /worker/proposals -> DurableGateway: authorize and persist
  -> Durable/Audit/HttpAuditCollector.cs -> AuditCollectorStore.cs: record audit evidence
  -> Restart gateway -> redeem approval -> retry same idempotency key
  -> Verify exactly one publication; stop services; retain state, audit and effects
Boundary: the collector owns its signing private key; approval is separate from worker proposals.
'@ }
        'phase7' { @'
Run-ResponseDrill.ps1 -> DurableChecks/Program.cs --drill
  -> DurableChecks/Fixtures/Phase7Checks.cs: incident and recovery scenarios
  -> Durable/Execution/DurableGateway.cs: durable authorization and revocation
  -> Durable/Budgets/DurableBudgetStore.cs + Execution/BoundedRunController.cs:
       enforce shared budgets, deadlines and cancellation in applicable scenarios
  -> Exercise audit outages, restart recovery, hanging tools and owned-process termination
  -> Assert actual effects and containment; retain artifacts/phase7/latest evidence
Boundary: synthetic executable checks, not a production shutdown command.
Completed earlier effects are checked separately from prevention of later work.
'@ }
        'phase8' { @'
Run-CollaborationLab.ps1 -> CollaborationChecks/Program.cs --demo
  -> CollaborationChecks/Fixtures/DemoRunner.cs: start broker and evaluator services
  -> Supervisor: issue scoped researcher/writer credentials and message routes
  -> Agents -> Collaboration/Broker/Broker.cs: enforce sender, recipient, topic and quotas
  -> Collaboration/Evaluation/IndependentEvaluator.cs: evaluate the synthetic submission
  -> Verify authorized method and measured messages/submission; stop services; retain evidence
Boundary: agents do not own the evaluator's expected answer or scoring authority.
This desktop demo uses local services; the isolated demo adds container boundaries.
'@ }
        'isolation' { @'
Run-IsolatedLab.ps1 -> deploy/isolation/compose.yml: build isolated services
  -> Worker/Isolation/IsolationChecks.cs: attempt filesystem/network bypasses
  -> Relay: fixed gateway route; container/network boundary constrains direct traffic
  -> Worker/Program.cs -> API /worker/proposals -> Gateway.cs + DefaultPolicy.cs
  -> Inspect container privileges/mounts and compare protected canary
  -> Assert two permitted reads, zero publications and unchanged canary
  -> Remove owned containers; retain build/probe diagnostics and artifact guide
Boundary: OS/container restrictions complement C# request authorization.
Docker builds container binaries; the host artifact helper requires a Release build.
'@ }
        'phase8-isolated' { @'
Run-IsolatedCollaborationLab.ps1 -> deploy/collaboration/compose.yml
  -> Collaboration/Program.cs -> Hosting/CollaborationApplication.cs: container modes
  -> Agent probes: private workspace, missing secrets and blocked alternate channels
  -> Researcher/writer -> relay -> Collaboration/Broker/Broker.cs: scoped messages
  -> Evaluation/IndependentEvaluator.cs + AuthorizedMethodEvaluator.cs:
       independently verify the answer and authorized method
  -> Assert two messages and one submission; remove owned containers; retain evidence
Boundary: agents cannot access evaluator credentials or private scoring material.
Docker builds container binaries; the host artifact helper requires a Release build.
'@ }
        'comparisons/suite' { @'
Run-ComparisonLab.ps1 (-Scenario selects what runs)
  -> Comparisons/Program.cs -> Scenarios/Portable/PortableScenarios.cs
       measure unsafe and protected behavior in selected portable cases
  -> For all/isolation/containment: Run-ContainerComparisons.ps1
       measure selected fake-secret transfer and late-write cases
  -> Read comparisons.json; require passing verdicts (exactly 12 pairs for all)
  -> Assemble summary.md and combined results; link selected evidence guides
Boundary: deliberate unsafe behavior stays inside synthetic fixtures.
A pair requires unsafe failure, secure prevention and successful positive controls.
Selecting portable does not run Docker cases.
'@ }
        'comparisons/container' { @'
Run-ContainerComparisons.ps1 -> deploy/comparisons/compose.yml
  -> Inspect outer container boundary and verify permitted relay reads
  -> Comparisons/Program.cs --container -> Scenarios/Containers/ContainerScenarios.cs
  -> IsolationScenario.cs: measure fake-secret transfer to local CaptureService.cs
  -> ContainmentScenario.cs: flag-only stop versus revocation/owned-child termination
  -> Verify selected effects and positive controls; save deployment/capture/result evidence
  -> Remove owned containers; retain summary and artifact guide
Boundary: the unsafe variant still runs inside outer containment.
No real secret or external receiver is used; earlier completed effects remain visible.
'@ }
    }
    if ($flow) {
        Write-Host ''
        Write-Host 'How this demo connects to the code' -ForegroundColor Cyan
        Write-Host $flow
        Write-Host 'C# paths above are relative to src/ or tests/; scripts and deploy paths are repository-relative.'
        Write-Host 'Rebuild Release after changing host-run C#: these scripts execute compiled DLLs.'
        Write-Host 'PowerShell coordinates and explains; C# and configured OS boundaries enforce the checks.'
    }

}
function Start-LabPresentation {
    param([string]$Case, [string]$Directory, [long]$RunNumber = 0)
    $purpose = switch ($Case) {
        'gateway' { 'A worker may read scoped documents, but cannot contact outsiders, change permissions or publish without approval.' }
        'document-gateway' { 'The gateway resolves host-owned document IDs; a worker never chooses arbitrary filesystem paths.' }
        'model-gateway' { 'Model output is an untrusted proposal. The gateway independently authorizes every requested action.' }
        'document-demo' { 'Demonstrate scoped reads of pinned synthetic files through the authenticated gateway.' }
        'model-demo' { 'Demonstrate that adversarial model proposals cannot expand the worker grant. Offline mode uses a synthetic fixture.' }
        'durable-demo' { 'Demonstrate persistent authorization, approval binding and independently verifiable audit evidence.' }
        'isolation' { 'Attempt filesystem and network bypasses from isolated workers, then verify that permitted gateway work still succeeds.' }
        'phase8-isolated' { 'Isolated agents collaborate through scoped messages; an independent evaluator verifies the answer and authorized method.' }
        'phase8' { 'Verify scoped agent collaboration, message restrictions and independent evaluation using synthetic tasks.' }
        'phase7' { 'Exercise incident response, revocation, deadlines and budgets; verify containment and recovery evidence.' }
        'comparisons/suite' { 'Compare unsafe and protected behavior: observe a real synthetic failure, verify prevention, then verify legitimate work.' }
        'comparisons/container' { 'Measure fake-secret transfer and late writes with safeguards disabled and enabled inside contained environments.' }
        default { 'Run the synthetic security demonstration and inspect its measured effects.' }
    }
    Write-Host "`nDemo: $Case$(if ($RunNumber) { " - Run #$RunNumber" })" -ForegroundColor Cyan
    Write-Host "Purpose: $purpose"
    Write-Host 'PASS means the expected security behavior was observed; a denied attack can be a successful check.'
    Write-Host 'Prerequisite: Release build; container demos also require Docker in Linux container mode.'
    $context = [pscustomobject]@{ Case=$Case; Directory=$Directory; Purpose=$purpose; Step=0; Started=[DateTimeOffset]::UtcNow }
    Write-LabCodeFlow -Case $Case
    Write-LabStage $context 'Prepare synthetic fixtures and scoped authority'
    return $context
}

function Write-LabStage {
    param($Context, [string]$Description)
    $Context.Step++
    Write-Host "`n[$($Context.Step)] $Description" -ForegroundColor Cyan
}

function Write-LabResult {
    param([object]$Line)
    $text = [string]$Line
    Write-Host $text
    $explanation = switch -Regex ($text) {
        '^ReadDocument: Allowed' { 'The requested document is inside this worker grant; useful work remains possible.'; break }
        '^ExternalRequest: Denied' { 'The attempted external request is outside the grant and was blocked.'; break }
        '^MessageAgent: Denied' { 'The worker has no grant for this communication; no agent message is authorized.'; break }
        '^ChangePermissions: Denied' { 'A worker cannot expand its own authority through a tool request.'; break }
        '^PublishReport: ApprovalRequired' { 'Publication needs a separately authorized approval; this proposal alone cannot publish.'; break }
        '^PASS .*unsafe|^PASS .*secure|^PASS [0-9].*failure observed' { 'Compare measured unsafe effects with prevention and the legitimate positive control in the saved evidence.'; break }
    }
    if ($explanation) { Write-Host "  Why it matters: $explanation" }
}

function Get-LabArtifactDescription {
    param([IO.FileInfo]$File)
    switch -Regex ($File.Name) {
        '^run.json$' { return 'Run number and lifecycle timestamps. Identifies the attempt; timestamps alone do not prove success.' }
        '^artifact-index.md$' { return 'Presenter evidence guide, completion result and links to retained files.' }
        'audit-public|public-key' { return 'Public verification key. Lets the operator verify signed audit evidence; it grants no signing authority.' }
        'manifest' { return 'Pinned document manifest. Binds scoped document IDs to approved synthetic content.' }
        'checkpoint|audit|\\.jsonl$' { return 'Audit/checkpoint evidence. Supports event ordering, integrity and recovery inspection.' }
        'summary|report|evaluation|verdict' { return 'Outcome or evaluation summary. Use it to explain measured results and acceptance criteria.' }
        '^effects.json$' { return 'Observed tool-effect counts from the operator endpoint. Confirms actual reads/publications rather than only policy decisions.' }
        'comparisons.json' { return 'Machine-readable comparison verdicts and measured effects. Shows unsafe failure, prevention and positive controls.' }
        'capture' { return 'Synthetic capture evidence. Shows whether the fake secret reached the contained receiver.' }
        'containment' { return 'Containment measurements. Shows stopped work, late effects and effects already completed.' }
        'deployment|inspect' { return 'Deployment inspection evidence. Supports the claimed container security configuration.' }
        'worker-decisions' { return 'Worker proposals and gateway decisions. Shows permitted reads, blocked actions and publication requiring approval.' }
        'isolation|worker' { return 'Worker/bypass probe evidence. Shows individual attempts against filesystem and network boundaries.' }
        'build|compose|gateway|collector|\.log$|diagnostics' { return 'Operational diagnostics. Use to troubleshoot startup, subprocess failures and service activity; not a standalone security verdict.' }
        'checkpoint|audit|\.jsonl$' { return 'Audit/checkpoint evidence. Supports event ordering, integrity and recovery inspection.' }
        'proposal' { return 'Untrusted proposed actions supplied to the gateway. Explains what was requested before authorization.' }
        'key|credential|token|state|\.pem$' { return 'Private local runtime material. Supports the fixture; do not publish or share without review.' }
        default { return 'Synthetic fixture or supporting evidence. Interpret alongside the demo result and its documented checks; existence alone is not proof.' }
    }
}

function Complete-LabPresentation {
    param($Context, [bool]$Succeeded, [switch]$Quiet)
    try {
        if (!$Quiet) { Write-LabStage $Context 'Review result and retained evidence' }
        $verdict = if ($Succeeded) { 'PASS' } else { 'FAILED - inspect the error and diagnostics; partial evidence is not a pass' }
        if (!$Quiet) { Write-Host "Result: $verdict" -ForegroundColor $(if ($Succeeded) { 'Green' } else { 'Red' }) }
        if (!(Test-Path -LiteralPath $Context.Directory -PathType Container)) { if (!$Quiet) { Write-Host 'No artifact directory was created.' }; return }
        $index = Join-Path $Context.Directory 'artifact-index.md'
        $metadata = Join-Path $Context.Directory 'run.json'
        if (Test-Path -LiteralPath $metadata) { $runInfo = Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json; if (!$Quiet) { Write-Host "Evidence attempt: Run #$($runInfo.RunNumber)" } }
        $lines = @('# Demo evidence guide', '', "Case: $($Context.Case)", "Run: $($runInfo.RunNumber)", '', "Purpose: $($Context.Purpose)", '', "Result: $verdict", '', 'The next run of this case overwrites this evidence. Copy it elsewhere before rerunning if needed.', '', 'Private runtime material must be reviewed before sharing. Links list files without reading or exposing their contents.', '', '| Artifact | Contents and significance |', '|---|---|')
        if (!$Quiet) { Write-Host 'Evidence (overwritten on the next run of this case):' }
        $files = @(Get-ChildItem -LiteralPath $Context.Directory -Recurse -File | Where-Object { !($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $_.FullName -ne $index } | Sort-Object FullName)
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($Context.Directory, $file.FullName).Replace('\','/')
            $description = Get-LabArtifactDescription $file
            $link = ([Uri]::new($file.FullName)).AbsoluteUri
            $label = $file.FullName
            if ($Host.UI.SupportsVirtualTerminal -and !$env:CI) { $label = "`e]8;;$link`e\$label`e]8;;`e\" }
            if (!$Quiet) { Write-Host "  $label`n    $description" }
            $encoded = ($relative.Split('/') | ForEach-Object { [Uri]::EscapeDataString($_) }) -join '/'
            $lines += "| [$relative]($encoded) | $description |"
        }
        $lines += '| [artifact-index.md](artifact-index.md) | This evidence guide and completion result. |'
        $related = switch ($Context.Case) {
            'document-demo' { '../../document-gateway/latest/artifact-index.md' }
            'model-demo' { '../../model-gateway/latest/artifact-index.md' }

        }
        if ($Context.PSObject.Properties['RelatedGuides']) { $related = $Context.RelatedGuides }
        foreach ($relativeGuide in $related) {
            $relatedPath = [IO.Path]::GetFullPath((Join-Path $Context.Directory $relativeGuide))
            if (Test-Path -LiteralPath $relatedPath) {
                $lines += "| [Related demo evidence]($relativeGuide) | Companion evidence guide. Check its run number and selected scenarios before combining results. |"
                if (!$Quiet) { Write-Host "  Companion evidence guide: $relatedPath" }
            }
        }
        [IO.File]::WriteAllText($index, ($lines -join "`n") + "`n")
        $guideLabel = $index
        if ($Host.UI.SupportsVirtualTerminal -and !$env:CI) { $guideUri = ([Uri]::new($index)).AbsoluteUri; $guideLabel = "`e]8;;$guideUri`e\$index`e]8;;`e\" }
        if (!$Quiet) { Write-Host "Evidence guide: $guideLabel" }
    } catch { Write-Warning "Could not finish the evidence guide: $($_.Exception.Message)" }
}
