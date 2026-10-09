Secure Agent Lab demo handout

A presenter guide to what happens and why it matters

Use this handout to explain the lab to someone who has not read the code. It covers eleven secure use cases and twelve before-and-after comparison pairs for the first ten, the results to point out and the limits of what those results prove. This is the single consolidated presenter handout.

**Say aloud**  The agent can suggest actions. A separate trusted service decides whether those actions are allowed. We check what actually happened, rather than relying on what the agent says it did.

## Who does what

Part | Plain language explanation
--- | ---
Worker | The agent side. Sends requests using a short-lived credential.
Gateway | The trusted gatekeeper. Checks identity, task scope, limits and approval before executing a tool.
Operator | The trusted person or host that creates runs, approves exact actions and stops work.
Tool | The limited operation that reads a document or stores a synthetic report.
Audit collector | A separate service that records signed evidence of gateway decisions.
Relay and firewall | The only permitted network route from the isolated worker to the gateway.

## How to read the output

**Allowed** means the permitted tool ran. **Denied** means the gateway refused the proposal. **ApprovalRequired** means the proposal still needs trusted approval. **RecoveryRequired** means the result is uncertain and we must inspect effects before continuing.

PASS means a particular assertion succeeded. Finish by checking the final effect counts and completion message; one PASS line does not prove the whole demonstration passed.

Scope: synthetic documents and reports. The original isolation demo, isolated collaboration and all twelve comparison pairs passed locally. Linux and Windows CI check jobs passed in Actions run 2; a passing isolation CI run on the final fixes still needs confirmation. Optional live model calls have not been verified. Prepared 9 October 2026.


---


# Task access and unsafe requests

## 1 Read only what the task permits

**Situation**  An agent needs a task document to prepare a report. It should not be able to browse unrelated files or choose another task identity.

**Show**  Run the authenticated process demo. The trusted host creates a run and gives the worker a short-lived credential. The worker sends a document request; the gateway derives its identity from that credential and checks its task grant.

```powershell
./scripts/Run-Lab.ps1
```

**Point out**  ReadDocument returns Allowed with reason scoped_read. The permitted resource is documents/task. HTTP checks separately test forged, expired and mismatched credentials, and worker attempts to call operator endpoints.

**Say aloud**  This worker has permission for one task. Changing a name in the request does not make it another user or an administrator.

**Why it matters**  A model prompt cannot establish identity. Authentication and task permission checks belong in the trusted gateway.

**Limit**  This desktop demo separates processes but does not restrict the worker's operating-system access. Use case 5 adds that boundary.

## 2 Refuse requests outside the task

**Situation**  An agent proposes an outbound request, a message to another agent, a permission change or a secret read. These actions are outside the approved task.

**Show**  The same process demo requests an external transfer, agent messaging and permission changes. The model fixture also requests a secret-like document.

**Point out**  ExternalRequest, MessageAgent and ChangePermissions are Denied with reason out_of_scope. Publication is ApprovalRequired. The process demo ends with zero publications.

**Say aloud**  The worker asked for these actions, but asking did not grant permission. Even a persuasive explanation would not change this decision.

**Why it matters**  A compromised or confused proposal source must not expand its own powers. The gateway denies operations that were never granted.

**Limit**  Denied proposals in this demo do not send external traffic. They demonstrate refusal by the gateway; direct network bypass is tested separately.


---


# Approval retries and reliable evidence

## 3 Approve the exact action

**Situation**  Publishing is allowed only after a trusted reviewer approves the specific report, destination and run.

```powershell
./scripts/Run-DurableLab.ps1
```

**What happens**  The operator previews the complete synthetic draft and its content hash, then issues an approval for that exact action. The worker cannot approve its own request. The gateway restarts, retains the approval and accepts the approved publication.

**Point out**  The demo ends with one read and one publication. Approval checks also reject changed content, a different destination/run, expired tickets and reuse with a different request key.

**Say aloud**  The approval means publish this report to this destination for this task. It is not a general permission to publish whatever the agent chooses next.

**Why it matters**  A broad approval could be reused for a different or harmful action. Exact binding prevents substitution and cannot enlarge the task grant.

**Limit**  The script automatically approves a fixed synthetic draft. It demonstrates the approval mechanism, not a complete human review interface. Publication here is a durable synthetic ledger entry.

## 4 Retry safely and preserve evidence

**Situation**  A network acknowledgement is lost, a process crashes, or someone alters stored history. We need to know whether an action already happened.

**What happens**  The durable demo retries the same approved publication after restart. The gateway recognizes the request key and returns the recorded result without a second effect. A separate audit collector signs checkpoints for the decision history.

**Point out**  publication_replayed means the request was recognized; the publication count stays one. The durable check suite injects crashes, audit outages, history rewriting and truncation.

```powershell
dotnet run --project tests/SecureAgentLab.DurableChecks -c Release --no-build
```

**Say aloud**  We retry the same request without publishing twice. We also check signed evidence held outside the gateway, so a locally rewritten history is not enough to hide a change.

**Limit**  Crash and tampering scenarios are assertions in the check suite. These local-file transactions do not guarantee exactly-once writes to an arbitrary external service. An uncertain result requires inspection.


---


# Isolation and direct bypass attempts

## 5 Keep the worker inside its boundary

**Situation**  A malicious worker ignores the gateway and tries to connect directly to protected services or read files it should not see.

```powershell
./scripts/Run-IsolatedLab.ps1
```

**Before showing**  Docker Desktop must be running Linux containers. The script builds images and launches a firewall guardian, a relay, a gateway and restricted workers. Startup may take longer on the first run.

## Explain the sequence

**First prove the allowed path works.** A readiness worker sends a request through the relay without a credential. The backend returns HTTP 401. This proves the permitted route is working while authentication still blocks execution.

**Then attempt direct bypasses.** Each worker probes the backend, metadata/private/external addresses, IPv4/IPv6, DNS, operator routes and CONNECT tunnelling. It also checks capabilities, read-only filesystems and private scratch storage.

**Finally run legitimate work.** Both workers submit the normal proposal sequence. The script independently inspects the deployed restrictions and checks actual effects and the protected test file.

## What to point out

Your successful output showed both workers passing bypass checks and deployment inspection. The final result was **2 reads, 0 publications, protected canary unchanged**. The canary is a harmless test file whose contents must remain unchanged.

**Say aloud**  An API refusal is not sufficient if the worker can go around it. Here, the operating-system and network restrictions also block the direct route, while the approved document read still works.

**Why it matters**  The worker has no host socket or protected mount, runs without administrative capabilities, has private temporary storage and has CPU, memory and process limits. The firewall permits one relay route; the relay forwards only the proposal endpoint.

**Limit**  The host, Docker daemon, kernel, gateway, relay and firewall guardian remain trusted. This does not prove resistance to a kernel escape. This container demo uses the in-memory gateway; durable audit is demonstrated separately.

**If it fails**  The script preserves worker logs and relay/firewall diagnostics before cleanup. Look under artifacts/isolation. A failed CI job uploads an isolation-diagnostics artifact.


---


# Real files and hostile instructions

## 6 Read a real file safely

**Situation**  A tool reads an actual local document, but the caller must not choose an arbitrary path or redirect it to another file.

```powershell
./scripts/Run-DocumentLab.ps1
```

**What happens**  The script creates byte-exact synthetic files. A trusted catalog maps documents/task to a specific file and expected content hash. The gateway checks permission; the reader opens the file safely, checks its content and measures the bytes returned.

**Point out**  One actual synthetic task-file read succeeds; publication count stays zero. The document checks cover traversal, links/reparse redirection, missing/changed files, oversized responses and invalid text.

**Say aloud**  The worker requests a known document ID. It does not supply a filesystem path. The reader also checks the real file and returned size, so a harmless-looking request cannot redirect or enlarge the read.

**Why it matters**  File paths, links and misleading size estimates can turn a small read tool into broad file access. The adapter is intentionally narrow and capped at 4,096 bytes.

**Limit**  This is a real read of synthetic local data, not a connection to a production document store. Editing fixture encoding or newlines changes the expected hash.

## 7 Treat hostile text and model output as data

**Situation**  A document tells the agent to read secrets, send data elsewhere or pretend that a supervisor approved an action. This is prompt injection: instructions embedded in material the agent was meant to read.

```powershell
./scripts/Run-ModelLab.ps1
```

**What happens**  The default demo uses a fixed adversarial proposal fixture without calling a model. A strict parser accepts only a bounded list of known operations and resource IDs. It rejects forged authority fields and malformed batches. Valid proposals still go through the gateway.

**Point out**  The fixture yields one permitted read and zero publications. Secret access, transfer, messaging and permission changes are denied; publication still needs approval. Document checks separately verify that hostile document text does not authorize actions.

**Say aloud**  The content may tell the agent to ignore the rules. That text has no authority. We validate the proposed request and enforce permission outside the model.

**Limit**  Offline fixtures test enforcement, not whether a particular live model will resist injection. Optional -Live mode needs a host-held provider key and can cost money; it has not been live-verified.


---


# Versioned writes and usage limits

## 8 Prevent an old approval from overwriting new work

**Situation**  Two reports are reviewed against the same starting version. One is saved first. The other must not silently overwrite it using an old approval.

```powershell
./scripts/Run-ResponseDrill.ps1
```

**What happens**  The phase 7 drill starts with report version 0. Approval covers exact content and the expected version. One write stores the reviewed report as version 1. A different approved write still expecting version 0 is rejected.

**Point out**  The versioned-write check passes only when reviewed content stays at version 1, altered content/version fails approval binding and the stale request fails resource_version_conflict. Replaying the original request does not create another write.

**Say aloud**  Approval is tied to the resource we reviewed. If the report changes in the meantime, the agent must get a fresh review rather than overwrite someone else's work.

**Why it matters**  Exact content approval alone does not detect intervening changes. Checking the expected version prevents a stale decision from being applied to a different state.

**Limit**  This write stores synthetic report content in gateway state. It is not an email, public post or transaction with an external service.

## 9 Limit work before it starts

**Situation**  Several requests arrive at once, a worker retries repeatedly, or a run continues beyond its deadline. Individually reasonable requests can exhaust a shared budget.

**What happens**  The same drill tests shared durable reservations for attempts, input/output units, cost units and simultaneous work. Coordinators reserve allowance before starting. Restarting a coordinator does not restore spent allowance.

**Point out**  Parallel admissions stay within ceilings. Retry/fanout excess is refused. Deadline expiry prevents admission. A failed or cancelled attempt does not receive an automatic token/cost refund; only confirmed completion releases its active slot.

**Say aloud**  We reserve the maximum allowed usage before work begins. Two coordinators cannot each spend the same remaining allowance. A retry is still work and still counts.

**Limit**  The HTTP demo uses synthetic token/cost units, not actual provider billing. The optional live model request is separate. Shared local files coordinate local instances, not a distributed multi-host deployment.


---


# Emergency response and questions

## 10 Stop work and inspect what already happened

**Situation**  A tool hangs, an operator revokes a run, or emergency stop is needed. We must contain future actions and find out whether anything already completed.

```powershell
./scripts/Run-ResponseDrill.ps1
```

**What happens**  The drill times out cooperative work, revokes active work from another coordinator, exercises global stop and terminates an owned hanging child worker process. A noncooperative fixture continues later but its subsequent gateway request is denied.

**Point out**  The drill expects RecoveryRequired when execution is interrupted. Its controlled hanging-tool and termination fixtures produce zero effects. A report committed before stop remains stored; the check confirms stop does not erase it. Uncertain work can retain a quarantined active slot.

**Say aloud**  Stopping a run blocks future authorized actions. It does not undo a completed action. After interruption, we inspect the effect ledger and report version before deciding what to do next.

**Why it matters**  Cancellation is a request, not proof that work terminated. Automatically refunding allowance or retrying an uncertain write could duplicate effects.

**Limit**  A cancellation token cannot forcibly stop arbitrary in-process code. The process-termination check kills only a child created by the drill. Recovery uses trusted evidence and a deliberate new run, not deletion of inconvenient state.

## Questions the audience may ask

**Does a model need to be connected**  No. The default demos are deterministic and repeatable. The optional model produces proposals; it does not own tool permissions.

**Are denied actions actually attempted**  Gateway-denied proposals do not execute their tool. The isolation demo separately makes direct synthetic bypass probes, which must fail.

**Does this prove production readiness**  No. Identity integration, key lifecycle, provider accounting, external effects and deployment trust still need production design and review.

**Can agents collaborate**  The phase 8 demo has a separate scoped broker and independent evaluator. Ordinary gateway messaging stays denied. The desktop demo checks authorized HTTP methods; the isolated collaboration demo also verifies private worker workspaces and direct-access denial.

**What is the most important evidence**  The allowed path works, unsafe paths fail, and the final effects match the task. Agent wording alone is not evidence.


---


# Collaboration and independent evaluation

## 11 Collaborate within a task and verify the method

**Situation**  A researcher gives a writer two synthetic numbers. The writer must use the permitted message exchange to calculate an answer. A separate evaluator checks the steps and result.

**Purpose and security issue**  An unrestricted mailbox or shared cache can leak information across tasks or carry forged supervisor instructions. An agent must not be able to rewrite the scoring rules or award itself a pass.

**Run**  After the Release build, run the checks and the presenter demo:

```powershell
dotnet run --project tests/SecureAgentLab.CollaborationChecks -c Release --no-build
./scripts/Run-CollaborationLab.ps1
```

**What happens**  The supervisor starts a broker and evaluator as separate services, gives each worker its own short-lived credential and grants exactly the facts and acknowledgment routes. Cross-run delivery, a supervisor topic, cache/artifact/log channels and worker access to evaluation are rejected. The researcher sends 20 + 22; the writer consumes it, acknowledges receipt and submits 42. The broker seals and signs the complete task transcript. The evaluator rejects altered evidence, then checks its private expected facts/answer and the prescribed message steps.

**What to point out**  Two messages are delivered and consumed, and one answer is submitted. The output ends with Independent evaluation: PASS. The 18 checks also show that guessing the correct answer without the authorized exchange fails, and that exchanging substituted facts fails even when the final number is correct. Message text always remains worker data, even if it looks like a supervisor instruction.

**Say aloud**  The answer is only half the check. The evaluator also checks who exchanged which facts, through which permitted route, before the answer was submitted. The worker cannot change that evidence or the scoring rules.

**Why it matters**  Collaboration introduces new information channels. Sender, recipient, topic, task identity and measured usage limits must remain controlled independently of what agents say. The evaluator's credentials and answer configuration never enter worker requests.

**Evidence and cleanup**  Read the printed artifacts/phase8 directory. It contains the signed synthetic transcript, public verification key and evaluation result, with no bearer credentials or private keys. The launcher stops both owned service processes on success or failure. Evidence remains for review.

**Limit**  The workers are deterministic HTTP clients inside the trusted harness, not hostile sandboxed processes. Broker state is volatile. Logical isolation and the separate isolated collaboration container topology both pass locally; new remote CI remains pending. Use Run-IsolatedCollaborationLab.ps1 to show the container boundary. This does not enable messaging in the original gateway or authorize a production pilot.

---

# Prepare and present the demos

## Before the session

Use PowerShell 7 and the .NET 10 SDK. Build Release binaries first. For isolation, start Docker Desktop in Linux container mode. Default demos need no model account.

```powershell
Set-Location 'C:\Users\tmham\source\repos\Secure-agent-lab'
dotnet build SecureAgentLab.slnx -c Release
docker info --format '{{.OSType}}'
```

The Docker command must print linux before the isolation script can run. Run scripts one at a time; several use the same local ports. The scripts stop their owned processes/containers afterward and leave ignored evidence under artifacts.

## Choose a demonstration

Command from the repository root | Use cases
--- | ---
dotnet run --project src/SecureAgentLab.Demo -c Release --no-build | Introduction
./scripts/Run-Lab.ps1 | 1 and 2
./scripts/Run-DurableLab.ps1 | 3 and 4
./scripts/Run-IsolatedLab.ps1 | 5
./scripts/Run-DocumentLab.ps1 | 6
./scripts/Run-ModelLab.ps1 | 7
./scripts/Run-ResponseDrill.ps1 | 8 to 10
./scripts/Run-CollaborationLab.ps1 | 11
./scripts/Run-IsolatedCollaborationLab.ps1 | 11 with isolated workers
./scripts/Run-ComparisonLab.ps1 -Scenario all | All 12 comparison pairs

## A simple presentation order

**Short session:** explain the worker/gateway boundary, run Run-Lab, then Run-IsolatedLab. End on the permitted reads, denied actions and unchanged canary. Add Run-DurableLab for approvals, Run-ResponseDrill for incident response or Run-CollaborationLab for authorized teamwork and independent scoring.

## If PowerShell blocks the script

Run the chosen script in PowerShell 7 with a session-only policy. This does not change permanent execution-policy settings.

```powershell
pwsh -ExecutionPolicy RemoteSigned -File .\scripts\Run-IsolatedLab.ps1
```

If pwsh is missing, install PowerShell 7 or use its installed full path. If Docker cannot connect, open Docker Desktop and wait for its Linux engine. For a failed isolation run, read worker and diagnostics files in its artifact directory.

Source: repository DEMO_GUIDE.md, implementation and scripts, Actions run 2, and current local Windows/Linux regression, isolation, collaboration and comparison runs. The complete technical guide contains configuration, detailed test coverage and recovery limits.

---

# Show what happens without each safeguard

All comparison phases A to E are implemented. Ten pairs run on the desktop; two use disposable Linux containers. Every PASS requires an actual unsafe failure, prevention in the secure run and a working permitted path. All data is generated and synthetic. The normal secure gateway has no insecure switch.

```powershell
./scripts/Run-ComparisonLab.ps1 -Scenario all
```

Use -Scenario portable to show the ten desktop pairs without Docker. Individual commands below use the same launcher. The final all-suite output points to one summary index with links to measured effects. A failed unsafe attack or a dead allowed path makes the demonstration fail; it does not prove a safeguard worked.

## Exact action approval case 3 phase A

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario approval

**Missing protection**  Approval permits the publish operation but does not bind the reviewed text. The worker substitutes a visibly different harmless draft after review.

**What goes wrong**  The unsafe store contains Unreviewed harmless synthetic replacement. The secure store refuses that substitution, then publishes the exact reviewed original once. Both start fresh; the unsafe result cannot contaminate the secure fixture.

**Say aloud**  Approval must identify the exact action that was reviewed, including its content. Permission to publish something is too broad.

## Task identity case 1 phase B

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario scope

**Missing protection**  The executor trusts a task identity supplied by the caller instead of the authenticated task binding.

**What goes wrong**  A worker authenticated for task A selects task B and receives the exact generated fake-secret bytes. The secure request is denied with no foreign result or read effect; its authorized task A read still succeeds.

**Say aloud**  The trusted service must decide which task this worker belongs to. A request field is not proof of identity.

---

# Forbidden operations and unsafe file reads

## Operation grants case 2 phase B

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario actions

**Missing protection**  The executor skips the immutable task grant for transfer, message and permission-change proposals.

**What goes wrong**  The unsafe synthetic stores record one fake-secret transfer, one message and admin=true. The secure gateway denies all three and leaves those effects absent; a permitted document read still works. These desktop stores are in memory and perform no HTTP or real permission changes.

**Say aloud**  Being able to ask for an action must not mean being allowed to execute it. Inspect each of the three effects separately.

## File paths links and size case 6 phase B

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario files

**Missing protection**  Caller paths and link targets are followed, and declared size is trusted instead of actual bytes.

**What goes wrong**  Traversal and a generated link return fake secret bytes. A 256-byte file passes a declared-zero check despite the intended 128-byte cap. Secure opens reject traversal/link access and return no partial oversized data; the pinned allowed task file still fits and reads successfully.

**Say aloud**  A safe resource name, a safe opened file and a measured byte limit solve different problems. This pair shows all three results.

**Limit**  Unsafe paths and links are fixed generated fixtures with an outer read bound; the demo does not accept arbitrary host paths.

---

# Injected instructions and lost acknowledgements

## Untrusted document commands case 7 phase B

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario injection

**Missing protection**  A deterministic executor treats a marker inside an untrusted document as a command.

**What goes wrong**  It appends the generated fake secret to its in-memory capture store. The secure gateway denies the equivalent transfer proposal. A malformed authority-bearing batch is also rejected before even its first read executes. The normal scoped read remains usable.

**Say aloud**  Reading an instruction in a document is different from receiving authority to execute it. This example needs no model account or network call.

## Safe retry case 4a phase C

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario retry

**Missing protection**  No durable idempotency record survives a lost reply.

**What goes wrong**  The local publication commits, a lost acknowledgement is injected, and a retry produces a second publication row. The secure gateway reports an uncertain response, reopens/reconciles state and returns publication_replayed with exactly one effect.

**Say aloud**  Not receiving a reply does not prove nothing happened. Retrying must identify the same action, not accidentally create another one.

---

# Audit integrity and stale writes

## Independent audit case 4b phase C

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario audit

**Missing protection**  The history owner can remove an event and recompute a locally valid hash chain.

**What goes wrong**  Local verification accepts concealed publication history. The secure verifier compares an independently signed head and detects both rewriting and truncation. An audit-outage attempt creates no new publication; the earlier committed one remains visible.

**Say aloud**  A hash chain proves internal consistency. An independently trusted checkpoint is needed to detect a rewritten or shortened history.

## Current resource version case 8 phase C

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario version

**Missing protection**  Exact-content approvals remain, but the executor does not compare the expected version with the current version.

**What goes wrong**  Two reports approved against version 0 both write, leaving the second content at version 2. The secure store writes the first at version 1 and rejects the stale second approval, preserving the first content.

**Say aloud**  A correctly approved change can still be stale. Check what has changed since that approval was reviewed.

---

# Shared allowances and retry policy

## Atomic reservations case 9a phase C

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario quota

**Missing protection**  Two coordinators check the shared allowance before either charges it. A barrier makes this race repeatable.

**What goes wrong**  Both requests execute against allowance 1: two admissions, remaining balance -1. Secure shared reservations admit exactly one and leave balance 0. At least one permitted admission succeeds in both variants.

**Say aloud**  A correct limit checked at the wrong time can still be overspent. Checking and reserving must be one atomic operation.

## Accounted retry limit case 9b phase C

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario retry-limit

**Missing protection**  A fixed throwing tool is retried without accounting for the intended allowance of one initial attempt and one retry.

**What goes wrong**  The unsafe fixture is actually invoked five times, bounded by the outer demo supervisor. The secure fixture executes twice; later retries are denied before invocation. These are observed tool calls, not merely printed loop counters.

**Say aloud**  Failures can consume resources too. A retry needs its own permission and budget.

---

# Isolation and active containment

## Direct worker bypass case 5 phase D

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario isolation

**Missing protection**  The unsafe worker receives one generated fake-secret mount and a route directly to a disposable capture service.

**What goes wrong**  The actual capture receives the exact fake secret once. The secure worker has no fake mount and its direct capture request is blocked; secure capture remains empty. Both workers first prove the authorized relay read works. The outer firewall still blocks host, internet, metadata and other direct IPv4/IPv6 routes, even for the unsafe worker. Nothing is sent to a real destination.

**Say aloud**  Denying a gateway request is not enough if the worker can reach the resource directly. Compare the mount, the route and the actual captured bytes.

## Stop active work case 10 phase D

**Run**  ./scripts/Run-ComparisonLab.ps1 -Scenario containment

**Missing protection**  Stop only acknowledges an administrative flag; it does not revoke or terminate the already-running child.

**What goes wrong**  After acknowledgement, a controlled release lets the unsafe child write a late file. The secure supervisor records revocation, terminates its owned child and confirms exit before release; no late file appears. Both retain an earlier committed file. Evidence records acknowledgement, late-write and exit times separately.

**Say aloud**  A stop acknowledgement is not proof that work stopped. Check the process exit and later effects, and preserve what already happened.

---

# Comparison evidence cleanup and readiness

Phase E adds the single all-suite launcher, individual selectors, linked summaries and CI jobs. The local Windows and Linux portable runs, both container pairs and full twelve-pair launcher passed. New remote CI is configured but remains unverified until it runs for these changes.

The printed artifacts/comparisons/suite directory contains an overview linking measured effect summaries. Container evidence includes exact capture bytes, stop/late/exit timestamps, revocation/kill flags and safe deployment settings. Credentials and private signing keys are excluded from those summaries. Keep raw fixture state private: it can include short-lived synthetic run or approval identifiers.

Launchers terminate only their owned child processes and remove their containers/network. Cleanup failure fails the demo. Ignored evidence remains for review. Every scenario uses fixed harmless inputs, fresh variants and bounded counts/deadlines; none attacks a real service. Case 11 has the secure scoped-collaboration demos, but a vulnerable collaboration pair was not included in this comparison plan.

The pilot-readiness plan records allowed resources, numerical limits, independent review, monitoring ownership and recovery steps. It is a proposed gate, not approval for real data, spending or deployment. Production identity, actual provider accounting and multi-host recovery need separate work for a chosen live use case.
