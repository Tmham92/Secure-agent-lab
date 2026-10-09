# Secure Agent Lab: complete demo guide

This guide explains every implemented demonstration, its security purpose, the commands to run it, the actions it actually performs and the evidence to inspect. It is extended at the end of each implementation phase, as required by `AGENTS.md`. Last updated: 8 October 2026, phase 7.

The lab explores a simple question: **can an agent propose an unsafe action without gaining the authority to execute it?** Model instructions, retrieved documents and model output are treated as untrusted input. A trusted gateway owns permission checks, quotas, approval and execution. The demonstrations use synthetic documents and reports. They do not attack a third-party system or deploy a production service.

## 1. Start here

Open PowerShell 7 in the repository and build Release binaries:

```powershell
Set-Location 'C:\Users\tmham\source\repos\Secure-agent-lab'
dotnet build SecureAgentLab.slnx --configuration Release
```

Prerequisites: .NET 10 SDK and PowerShell 7. Docker Engine running **Linux containers** is additionally required for isolation. Only the explicitly selected live model mode needs a provider account, an API key and potentially paid usage. Every other demo works without model credentials or internet calls at runtime, after dependency restore/build.

For a short presentation, run these individually in this order:

```powershell
dotnet run --project src/SecureAgentLab.Demo --configuration Release --no-build
./scripts/Run-Lab.ps1
./scripts/Run-DurableLab.ps1
./scripts/Run-DocumentLab.ps1
./scripts/Run-ModelLab.ps1
./scripts/Run-ResponseDrill.ps1
```

Run the isolation demonstration separately when Docker is ready:

```powershell
docker info
./scripts/Run-IsolatedLab.ps1
```

These scripts finish rather than leaving a demo server running. Wait for one to finish before starting another that uses the same port. They locate binaries relative to their script directory, generate synthetic fixtures/credentials as needed and fail with a nonzero error when their assertions fail.

### Current implementation and verification

| Phase | What exists | Evidence and remaining acceptance |
|---|---|---|
| 1 | Deterministic policy gateway, mock effects, approval, quotas, stop and hash chain | 17 local executable checks; offline demo |
| 2 | Separate authenticated API and worker; operator role | 16 local HTTP/credential checks; desktop OS access remains unrestricted |
| 3 | Durable approvals, idempotency, crash recovery and external signed audit | 20 local durable checks and process demo; local filesystem transactions only |
| 4 | Linux isolation deployment, externally enforced egress and independent probes | Source/Compose validation; Docker runtime acceptance pending because local Engine is unavailable |
| 5 | Real bounded reads of pinned synthetic local files | 14 Windows checks and process demo; two additional Linux-specific checks await runtime/CI |
| 6 | Strict model proposal parser and optional host-side Responses generation | 12 offline model checks and adversarial process demo; no live API call made |
| 7 | Versioned synthetic report writes, shared runtime reservations and containment drill | 13/13 local phase 7 checks, including authenticated HTTP write and owned worker termination; see section 8 |
| 8 | Scoped collaboration broker and independent evaluator | Planned, conditional on a multi-agent use case; not implemented |

Local builds of existing projects use their restored assets. Full SDK restore of the phase 6 projects is currently blocked by access to the user NuGet configuration. Those projects were checked with the installed .NET compiler and framework references instead; that is not a successful full solution restore. Remote GitHub Actions, Linux document checks, container bypass tests and live-provider behavior remain unverified. Git metadata permissions have blocked staging/publication; local work does not imply a published commit.

## 2. Architecture and trust boundaries

```mermaid
flowchart LR
    D[Untrusted document/model output] --> W[Worker: proposals only]
    W --> G[Trusted gateway: authenticate, authorize, reserve]
    O[Trusted host/operator] --> G
    G --> E[Scoped document reader / report state]
    G --> A[Separate signed audit collector]
    A --> C[Independent checkpoint storage]
    H[Optional trusted model host] --> D
```

The diagram describes logical roles. Only the process and container demos create separate processes. The offline demo shares a process. In the container deployment the network boundary and relay add restrictions around the worker. A private C# method or interface alone does not constrain hostile code.

### Repository components

| Component | Purpose and authority |
|---|---|
| `src/SecureAgentLab.Core` | Typed proposals, immutable task grants, default-deny policy, deterministic/model proposal sources, safe document adapter and offline gateway |
| `src/SecureAgentLab.Demo` | Single-process walkthrough; host performs approval and stop |
| `src/SecureAgentLab.Transport` | Educational HMAC credential issuer/validator; operator and worker identities |
| `src/SecureAgentLab.Api` | HTTP boundary; worker proposal route and separate operator routes; durable mode is the normal API mode |
| `src/SecureAgentLab.Worker` | Holds a short-lived worker bearer credential and submits proposals; optional independent isolation probes |
| `src/SecureAgentLab.Operator` | Trusted host utility for issuing runs and operator requests; never put it or its signing key in the worker |
| `src/SecureAgentLab.Durable` | Filesystem-backed sessions, approvals, report/effect ledger, externally verified recovery and runtime budgets |
| `src/SecureAgentLab.AuditCollector` | Separate collector API and RSA-PSS signed checkpoints; collector write secret and signing private key are trusted configuration |
| `src/SecureAgentLab.ModelHost` | Optional trusted provider client; reads the provider key outside the worker and writes validated proposals |
| `deploy/isolation` | Container build, Compose services, trusted network guardian, firewall and fixed-route relay |
| `fixtures` | Synthetic model/document source material, including hostile instructions represented as data |
| `tests` | Dependency-free executable security harnesses; run them as console programs, not through `dotnet test` |
| `scripts` | Reproducible presentations, fixture creation and response drill |
| `.github/workflows/ci.yml` | Linux checks, separate Docker isolation job and Windows checks/drill; configuration alone does not prove CI passed |

Trust the gateway host, operator, issuer, executor, audit collector, isolation guardian, Docker daemon and operating-system kernel. Distrust model output, worker proposals and retrieved text. A compromise of the trusted host, signing keys or kernel is outside the protection claimed here.

### Resources and operations

The permitted task resource is `documents/task`; grants can also explicitly permit the hostile synthetic `documents/reference`. The publication destination is exactly `reports/draft`. No caller-supplied filesystem path is accepted by the reader. Secret-like resources, arbitrary HTTP destinations, agent messaging and permission changes are denied.

The operations are `ReadDocument`, `PublishReport`, `ExternalRequest`, `MessageAgent` and `ChangePermissions`. Model proposal JSON uses these exact string names. HTTP request records currently serialize enums numerically by default: respectively 0 through 4. Outcomes are `Allowed`, `Denied`, `ApprovalRequired` and `RecoveryRequired`. The reason and measured effects matter more than an agent's explanation of its intention.

## 3. Phase 1: deterministic authorization walkthrough

**Purpose:** introduce the host-owned gateway and show that proposal generation and execution authority are separate. **Goal:** readers recognize that an unsafe request can be denied even if an agent insists it is necessary.

**Security issue:** treating tool access as equivalent to a prompt saying “be safe” allows unsafe instructions to become actions. Task scope must be enforced by code outside the proposal source.

**Trigger:**

```powershell
dotnet run --project src/SecureAgentLab.Demo --configuration Release --no-build
```

The host creates a five-minute run with 20 calls and 4,096 response bytes. The scripted worker asks to read the task, make an external request, post a shared-cache message, change permissions and publish a report. The gateway allows the task read, denies the unsafe operations and requests approval for publication. The host supplies an exact-action ticket; one publication succeeds. Replaying the ticket fails. The host stops the gateway and the next read fails.

**Expected result:** final audit verification is true; effects are **one document read and one publication**. The program prints a label, outcome and reason for each action. Calls and actual returned UTF-8 bytes are limited, using a trusted clock for expiry.

**Actual effects:** the document and report are synthetic in-memory tools. No report is sent to an external service and no arbitrary URL is contacted. The “external request” is a denied proposal, not a network attack.

**Evidence/cleanup:** console output and in-memory audit; the process exits and loses its state. The original chain detects inconsistent hashes but does not independently prove that a valid prefix has not been truncated or the entire history rewritten. Phase 3 addresses that issue. In-process interfaces are a design boundary, not hostile-code isolation.

## 4. Phase 2: authenticated worker and gateway processes

**Purpose:** demonstrate an actual transport boundary. **Goal:** the worker cannot choose its run identity or obtain operator authority by changing JSON.

**Security issue:** accepting identity, grants or approval instructions from worker fields lets an untrusted caller impersonate a supervisor. The API derives run identity from a signed, authenticated credential and separately authorizes operator endpoints.

```powershell
./scripts/Run-Lab.ps1
# Alternative unused loopback port:
./scripts/Run-Lab.ps1 -Port 5200
```

The trusted script generates an ephemeral signing key, starts the gateway in explicit in-memory demo mode, obtains a run and worker credential, launches the worker, queries effects as an operator, stops the gateway and cleans up its child process. Provider keys are removed from the process environment before worker/gateway launch and restored in the parent afterward.

**Expected result:** the worker's task read succeeds; unsafe proposals are denied; publication remains approval-required. The script asserts **zero publications**. Its optional `-ExpectedDocumentReads` parameter checks an exact read count. Server logs are saved under ignored `artifacts` files for that run.

The credential protocol is custom versioned HMAC-SHA256, **not JWT/OIDC**. Validation fixes the algorithm and checks issuer, audience, role, subject, issued-at, expiry and grant fingerprint. The trusted issuer key can mint either role; a worker receives only its short-lived bearer credential. There is no production identity federation or individual operator-token revocation.

HTTP is permitted only through an explicit synthetic loopback option; otherwise requests require HTTPS. Authentication protects API routes, but a desktop worker still has the launching user's OS/network capabilities. Run the isolation demo to investigate independent bypass attempts.

## 5. Phase 3: durable approval, restart and signed audit

**Purpose:** prove that approval consumption and effects survive process restart, and that an independently anchored audit is required. **Goal:** a retried approved write produces one effect and history tampering does not silently become valid.

**Security issues:** approving only an operation name permits content substitution; a crash between approval and execution can duplicate effects; a process-local chain can be rewritten or truncated by its owner.

```powershell
./scripts/Run-DurableLab.ps1
# Both ports must be distinct and unused:
./scripts/Run-DurableLab.ps1 -GatewayPort 5201 -AuditPort 5202
```

The script creates a fresh ignored run directory and ephemeral issuer, audit-write and RSA signing keys. It starts a separate collector and gateway. The operator requests a preview containing the complete synthetic draft and its content hash, approves that exact draft, and lets the worker read its task. The gateway is restarted with the same state and in-memory keys. The approved publication succeeds once; the same idempotency request is replayed without a second effect.

**Expected result:** **one read and one publication**, durable approval survives gateway restart, and identical retry reports `publication_replayed`. This script automatically approves a fixed synthetic draft for demonstration; it is not an interactive human review workflow.

Approval binds the run, operation, destination, exact content hash, estimate, policy/grant context and expiry. It cannot expand the grant. Changing content or identity fails; another idempotency key cannot reuse a consumed approval. A matching replay still consumes request/response quota, so idempotency is not a quota bypass.

Each state transition prepares an authenticated pending record, obtains the collector's signed event/checkpoint, commits gateway state and removes the pending record. Checks inject crashes after prepare, after audit acknowledgement and after state commit. Recovery reconciles the prepared transition with the independently signed head. An uncertain acknowledgement requires inspection/recovery; it is not proof that nothing happened.

**Evidence:** `artifacts/durable-demo/<run>/` contains gateway state, collector log/checkpoint directories, public verification key and process logs. State contains authorization metadata; audit entries redact raw bearer credentials, approval tickets and document/report bodies, retaining hashes, reason, correlation and measured usage. Raw approved report content is stored only in phase 7 report state when that mode is enabled.

**Cleanup/recovery:** the script stops its processes and restores parent environment variables. Evidence remains. Private signing keys are not persisted by the demo, so do not try to reuse the old store by launching a new script with new keys. A real deployment needs protected key retention/rotation and filesystem permissions. This local-file transaction design is not a distributed database, certified power-loss protocol or exactly-once transaction with an arbitrary external service.

## 6. Phase 4: isolation and independently enforced egress

**Purpose:** close direct execution paths that an API denial does not constrain. **Goal:** demonstrate failed direct network/filesystem/control-plane bypasses from the worker and inspect the deployed restrictions.

**Security issue:** a malicious worker might ignore the gateway and directly read host files, contact metadata/internal services or use another run's storage. Authorization is ineffective if protected targets remain directly accessible.

```powershell
./scripts/Run-IsolatedLab.ps1
```

The script builds images, creates synthetic protected document fixtures and a secret canary, starts a trusted namespace guardian and fixed-route relay, starts the gateway and two workers, runs independent worker probes, inspects container configuration, checks gateway effects/canary integrity and removes the Compose deployment in cleanup.

The guardian installs default-deny IPv4/IPv6 OUTPUT rules using owner UIDs. Worker UID 1654 can reach only the loopback relay on 8080. Relay UID 1655 can reach the fixed gateway port 5188 and reply to the worker. Trusted gateway/operator UID 1656 has its required local path. The containers share the guarded network namespace but have separate process/mount namespaces. The relay accepts only the precise proposal POST route; operator routes, arbitrary proxying, queries and CONNECT are rejected. It uses a fixed upstream rather than a worker-controlled hostname, avoiding an arbitrary DNS/redirect proxy.

Workers run non-root with a read-only root filesystem, dropped capabilities, no Docker socket, private 8 MiB temporary storage, 128 MiB memory, 0.5 CPU and 32 process limit. Protected documents are mounted in the gateway only. The trusted guardian has NET_ADMIN authority to install enforcement; the worker does not.

**Expected result when run:** both workers can perform their scoped task read; **two reads, zero publications**, protected canary unchanged, and the independent probe/assertion set passes. The checks examine direct access, operator routes, capability/process exposure, workspace access and forbidden destinations, in addition to gateway decisions.

**Evidence/cleanup:** synthetic fixtures/logs live under `artifacts/isolation/<project>/`; Compose services and associated deployment resources are brought down by the script. Images and evidence can remain. See `deploy/isolation/README.md` for exact service/firewall details.

**Verification limit:** these runtime probes have not run locally because the Docker Engine is unavailable. A valid Compose configuration is not bypass evidence. Containers depend on the trusted kernel, daemon and guardian; they do not protect against a kernel escape or hostile host administrator. The normal desktop demos do not inherit these restrictions.

CI troubleshooting update, 9 October 2026: the supplied Linux runner output reached healthy containers and passed the gateway IPv4/IPv6/protected-storage positive controls, then failed because the DNS probe did not handle an explicit socket permission denial on send. The probe now accepts `SocketError.AccessDenied` during send/receive, or timeout, as blocked DNS; a received response still fails, and unrelated socket errors still surface. The send uses the same cancellation deadline as the receive. This partial CI output does not establish a passing full isolation run; rerun the job after this correction.

Follow-up analysis of Actions run #2 (`17141cc`) confirmed that Linux and Windows check jobs pass, including full solution builds, model/document checks and the response drill. Isolation progressed past DNS but timed out on its first relay HTTP request. The isolation launcher now first runs a separate `--relay-readiness` worker under the same container restrictions. It sends an unauthenticated POST through `127.0.0.1:8080` and requires backend HTTP 401; ten bounded attempts allow startup without treating an unreachable relay as success or executing a tool. Proxy use is disabled for these direct loopback probes. The script then runs its original two workers and still expects exactly two reads and zero publications.

Worker output streams to the console and `worker-readiness.log` / `worker-1.log` / `worker-2.log` under the run's artifact directory, even on failure. Before cleanup, failure handling collects Compose service status, relay logs/identity/config validation and IPv4/IPv6 OUTPUT rule counters into `diagnostics-*.txt`. CI preserves only those diagnostic files in an `isolation-diagnostics` artifact for seven days. It does not export complete container inspection, environment, issuer responses or Compose configuration, which could contain credentials. Diagnostic command failure does not deliberately replace the original demo error. This instrumentation still requires a container CI rerun to establish relay behavior and full isolation acceptance.

Local verification of this follow-up: Worker Release build passed with zero warnings/errors; PowerShell syntax parsed; an actual worker process against a synthetic loopback HTTP fixture retried 503 and succeeded on 401; a mocked Compose failure preserved worker output, saved safe relay diagnostics before cleanup and retained the original failure. These checks validate the new readiness/diagnostic behavior without claiming that the actual container relay path is fixed.

## 7. Phases 5 and 6: real reads and hostile model proposals

### Phase 5: bounded file-backed document tool

**Purpose:** replace a mocked read with a small real adapter. **Goal:** authorize exact task resources while safely opening and measuring the real bytes returned.

**Security issues:** path traversal, cross-task reads, symlink/reparse redirection, special-file hangs, oversized/invalid content and hostile text masquerading as instructions.

```powershell
./scripts/Run-DocumentLab.ps1
./scripts/Run-DocumentLab.ps1 -Port 5203
```

The script writes byte-exact synthetic fixtures and runs the authenticated process demo with `Lab:DocumentRoot` set to that directory. A host-owned catalog maps exact IDs to relative names and pinned SHA-256 content. The worker supplies an ID, not a path. Reads are bounded to 4,096 bytes, strict UTF-8 and regular files; missing, changed, malformed or oversized content is denied without partial output or disclosure of host paths.

On Linux the adapter uses handle-relative `openat`, no-follow flags and regular-file checks, including nonblocking treatment of FIFO candidates. On Windows it pins directory handles, validates ancestry and uses relative native opens that reject reparse redirection. Linux-only probes are additional to the Windows set.

**Expected result:** one real synthetic task-file read, **zero publications**. A document's embedded instruction to read secrets does not acquire authority. Explicitly granting `documents/reference` permits reading its hostile text as data, not executing it.

For manual fixture generation:

```powershell
./scripts/New-DocumentFixtures.ps1 -Destination 'C:\Users\tmham\source\repos\Secure-agent-lab\artifacts\manual-documents'
./scripts/Run-Lab.ps1 -DocumentRoot 'C:\Users\tmham\source\repos\Secure-agent-lab\artifacts\manual-documents' -ExpectedDocumentReads 1
```

Do not hand-edit fixture encoding/newlines and expect the pinned digest to match. Evidence is in ignored artifacts; scripts leave generated fixtures for inspection. See `docs/document-adapter.md`. This is a local synthetic read integration, not an unrestricted filesystem tool or external credential-backed document service.

### Phase 6: model-output validation and injection exercise

**Purpose:** keep authorization independent of how proposals are generated. **Goal:** identical gateway guarantees apply to adversarial model-like outputs and deterministic outputs.

**Security issue:** a model may repeat a document's malicious instructions, forge approvals, invent credentials or request secret access/exfiltration. Free-form model text must not become executable authority.

Run the default offline exercise:

```powershell
./scripts/Run-ModelLab.ps1
./scripts/Run-ModelLab.ps1 -Port 5204
```

The fixture proposes a permitted task read, secret access, outbound transfer, messaging, permission elevation and publication. The strict parser validates the entire batch before any submission: bounded JSON, 1–8 operations, exact operation strings, bounded resource identifiers and no duplicate/unknown properties. Only operation/resource are accepted. Content, ticket, grant, version and authority fields are rejected rather than trusted. Malformed batches have no partial execution.

**Expected result:** **one read and zero publications**; unsafe operations denied and publication approval-required. The worker reads a validated proposal file through `LAB_MODEL_PROPOSALS_FILE`; absent configuration retains deterministic behavior. Offline hostile fixtures exercise authorization, not the empirical likelihood that a particular live model resists injection.

Optional live mode requires an explicit model and a provider key in the trusted host environment:

```powershell
# Configure OPENAI_API_KEY securely in this host process; do not put it in source or arguments.
./scripts/Run-ModelLab.ps1 -Live -Model '<available-model-id>'
```

This performs an explicitly selected provider request and may incur charges. The model host uses the fixed HTTPS Responses endpoint, disables redirects/proxying in its normal client, requests strict structured output, sets `store:false`, supplies no tools and caps output tokens, response bytes and deadline. The prompt includes synthetic task/reference data, not lab credentials. Incomplete, refused, malformed or unexpected responses fail closed. Valid proposals are then sent through the same worker/gateway path. A live model may select a different permitted batch; inspect its decisions and measured effects rather than assuming the offline sequence.

The worker refuses a nonempty `OPENAI_API_KEY`; the launcher removes the host key before spawning worker/gateway. Validated output files are ignored artifacts. No live call has been verified locally. The model host is a trusted preprocessing step, not an autonomous worker loop. Its provider call is **not** automatically metered by the phase 7 synthetic tool-admission budget. See `docs/model-adapter.md` for client boundaries and failure cases.

## 8. Phase 7: supervised versioned report and response drill

**Purpose:** introduce one real local synthetic write and bound its execution. **Goal:** reviewed content cannot overwrite a changed resource, replicas cannot independently overspend shared quotas, and interruption has inspectable effects.

**Security issues:** stale approvals, retry duplication, concurrent quota races, runaway fanout/retries, hanging tools and the mistaken assumption that revoking a credential undoes a completed write.

```powershell
./scripts/Run-ResponseDrill.ps1
# Equivalent harness entry point:
dotnet run --project tests/SecureAgentLab.DurableChecks --configuration Release --no-build -- --phase7
```

This is a scripted security exercise with assertions, not a persistent interactive server. It creates fresh gateway/collector/budget fixtures in `artifacts/phase7/<run>/`, uses ephemeral keys and prints PASS/FAIL for each scenario. One scenario starts a real loopback HTTP API; another starts and terminates an owned hanging child .NET process. No arbitrary user process is terminated.

### The actual write

Versioned mode starts with report version 0 and no content. A `PublishReport` proposal contains the exact synthetic text and `ExpectedVersion:0`. Preview shows text, hash, destination, policy version and expected version. An operator issues an exact-content ticket. The successful write atomically stores that reviewed content as version 1, consumes the ticket and records the idempotency effect in gateway state. The canonical report is the versioned publication record in `state.json`; it is not a second independently written report file or remote service.

Two proposals can be approved against version 0. Once one commits, the other fails `resource_version_conflict` without overwriting version 1. Changing content or expected version invalidates ticket binding. Retrying the same approved request/key returns `publication_replayed` and leaves version/content unchanged. Crash recovery at all three durable transition boundaries yields the same one committed version/effect.

Versioned writes are opt-in through `Lab:EnableReportWrites=true`; legacy demos retain their original synthetic ledger semantics. The operator-only `GET /operator/report` exposes the current content/version. The worker cannot read that endpoint with its worker credential. Versioned proposals are rejected in the in-memory gateway and non-versioned durable mode. The strict model format does not gain write content/version authority through this addition.

### Runtime limits and cancellation

`DurableBudgetStore` stores HMAC-authenticated quota state with a local cross-instance filesystem lock. A run has a UTC deadline, attempt count, input/output token upper bounds, cost in integer micro-units, maximum concurrency and maximum retry ordinal. A trusted coordinator supplies an `ExecutionQuote`; the worker does not get to choose the reservation price.

Before a tool begins, the store reserves the upper bounds atomically. Rejected active admissions also consume an attempt. Charged token/cost budgets are never refunded on failure, cancellation or crash. Completion releases only the concurrency slot. Duplicate run creation cannot reset its quota; retry/fanout/token/cost/deadline exhaustion prevents admission. A restart preserves reservations. An interrupted noncooperative job retains a quarantined slot, since its termination is uncertain.

The controller propagates cancellation and watches shared deadline/revocation/stop state approximately every 50 ms. Timeout or interruption writes a gateway revocation marker and returns `RecoveryRequired` with `execution_interrupted_inspect_effects`. The drill's 150 ms timeout fixtures assert bounded completion with a generous three-second ceiling; this is a fixture measurement, not a universal production SLA. Arbitrary in-process code cannot be forcibly cancelled by a token. The noncooperative fixture is released later and its subsequent gateway read is denied. The owned-process fixture explicitly kills its child on cancellation and verifies that it exited.

Optional API admission uses `Lab:EnableRuntimeLimits=true` with durable storage. Runs receive deadline equal to grant expiry, attempts equal to call allowance (minimum one), input/output ceilings 1,000 each, cost ceiling 10,000, concurrency one and retries zero. Each HTTP proposal reserves **one synthetic input unit, one synthetic output unit and one cost micro-unit**, with a two-second tool timeout. These are educational admission units, not measured model tokens or real currency. Grant call limits and actual returned UTF-8 byte limits remain separately enforced. There is no automatic retry loop.

For a real provider/external tool, a trusted estimator and measured reconciliation, reviewed pricing, production cancellation/process isolation and an external transactional effect strategy would still be needed. Shared local-file reservations do not provide multi-host distributed coordination. Authentication detects modified quota bytes but has no independent anti-rollback checkpoint; protect the trusted runtime directory against deletion/rollback. Budgets and tool state use separate transactions: an uncertain/failed execution may conservatively consume its reservation.

### Drill scenarios and evidence

| Scenario | Observable result |
|---|---|
| Exact review plus stale approval | Version 1 retains the reviewed content; changed/stale proposal rejected |
| Crash after prepare, audit acknowledgement or state commit | Recovery yields one versioned effect; retry does not duplicate it |
| Audit unavailable before write | Denied and no report mutation; revocation marker remains usable |
| Restart/replica budget, retry and fanout | Shared limits preserved; quota cannot reset; excess admissions denied |
| Parallel spending | Successful reservations remain within ceilings; balances match actual admissions |
| Missing/malformed quota state | Authorization dependency failure before work |
| Hanging cooperative tool | Timeout, revocation and zero effects |
| Another coordinator revokes/stops | Active work receives cancellation; zero fixture effects |
| Noncooperative late tool | Uncertain slot quarantined; late proposal denied |
| Owned hanging worker | Cancellation terminates child process; zero effects |
| Authenticated HTTP publication | One versioned write, runtime unit charged; worker report endpoint forbidden |

The drill writes `evidence.json` with time, individual scenario status/duration and total/passed counts, plus the underlying synthetic gateway, signed audit and budget fixture files. Each PASS follows its effect/version/quota assertions. Do not infer that every directory is a reusable deployment: keys live only for the harness invocation.

### Containment and recovery rules

1. Stop/revoke markers constrain future gateway execution even if audit reconciliation is temporarily unavailable. API stop/revoke signals the marker before waiting on normal state/audit reconciliation.
2. Cancel active work and, when appropriate, terminate the specifically owned worker process. A cancellation request alone does not prove termination.
3. Preserve evidence and inspect the report version/content, effect ledger, pending state and independent collector head. A prepared transition may recover to a committed effect.
4. Resume only through a deliberate fresh run and reviewed state/key recovery. Do not delete a reservation or reuse an approval merely to get past a denial.
5. **A previously committed report remains after stop.** Revocation does not roll it back. Correcting it requires a separately authorized versioned write. Nothing in the drill claims reversal of an irreversible external action.

If a tool is still holding the gateway transaction lock or the collector is unavailable, control-event audit reconciliation can wait until that dependency becomes responsive. The marker is the immediate containment signal; the complete signed control record is reconciled afterward. There remains a race with already prepared/committing work, which is why interrupted execution requires effect inspection.

## 9. Run the checks and inspect evidence

After a normal Release build:

```powershell
dotnet run --project tests/SecureAgentLab.Checks --configuration Release --no-build
dotnet run --project tests/SecureAgentLab.TransportChecks --configuration Release --no-build
dotnet run --project tests/SecureAgentLab.DurableChecks --configuration Release --no-build
dotnet run --project tests/SecureAgentLab.DocumentChecks --configuration Release --no-build
dotnet run --project tests/SecureAgentLab.ModelChecks --configuration Release --no-build
dotnet run --project tests/SecureAgentLab.DurableChecks --configuration Release --no-build -- --phase7
```

Checks exit nonzero on failure. The first five harnesses cover 17 core, 16 transport, 20 durable, 14 Windows document and 12 model scenarios; Linux adds its platform-specific file checks. The phase 7 entry point is separate from the original durable suite so earlier behavior remains explicit.

Do not substitute a model's “I complied” message for evidence. Inspect gateway outcomes and the synthetic effect counter, real returned bytes, report content/version, approval consumption and independently signed audit. Denied proposals must not produce the prohibited effect. A synthetic “publication” in older demos is a ledger count; in phase 7 it additionally stores reviewed synthetic report text in the gateway's protected state.

Generated artifacts, credentials and build output are ignored by Git. Script cleanup stops owned processes/Compose services and restores its environment; artifacts intentionally remain for inspection. Logs and state still deserve access control even when secrets are redacted. Demo private keys are ephemeral; a state directory without its original key material is evidence, not a recoverable operational deployment.

## 10. Troubleshooting and limitations

| Symptom | What to check |
|---|---|
| Release binary missing | Build the solution first; scripts do not silently restore/build everything |
| NuGet configuration access denied | Restore/build from a normal authorized terminal; a successful `--no-restore` build only covers existing restored projects |
| Port already in use | Choose the script's alternate port options; durable mode needs two different free ports |
| Docker daemon/pipe unavailable | Start Docker Engine with Linux containers, verify `docker info`, then run isolation; desktop demos remain usable |
| Worker gets 401/403 | Credential expiry, issuer/audience, grant binding and role; operator endpoints require an operator credential |
| Approval mismatch/version conflict | Compare exact reviewed text, destination, run, estimate, policy and expected version; obtain a fresh review rather than weakening binding |
| Document digest mismatch | Use the fixture generator and exact synthetic catalog; encoding/newline edits change the digest |
| Dependency-unavailable/503 | Inspect collector availability, verification key, stream ID and checkpoint/state consistency; do not bypass audit |
| RecoveryRequired | Inspect pending transition, actual effects and collector head; retry only with the same approved binding/idempotency where appropriate |
| Runtime denial | Inspect remaining attempts/tokens/cost, deadline, active/quarantined jobs, retry policy and stop/revoke state |
| Git index.lock permission denied | Repository files can be writable while Git metadata remains denied; no commit/push is complete until Git reports success |

Synthetic report storage, custom lab identity, local locks and demonstration key lifecycle do not constitute production readiness. The optional live model request has separate provider-side behavior/cost; offline tests cannot verify it. Historical incident background in README came from the supplied design package and is not independently established by these demos.

## 11. Next phase and guide maintenance

Phase 8 is conditional: introduce authenticated sender/recipient/topic grants and quotas for an agent-message broker only if multiple agents are needed, with isolated workspaces and an independent evaluator whose answers/scoring credentials are outside agent reach. There is currently no agent collaboration capability authorized by a model proposal.

After every finished phase, extend this guide before reporting completion. Add the purpose, learning goal, security issue, exact command, prerequisites, actor sequence, expected output, actual effects, evidence paths, cleanup/recovery and residual limits. Update the status matrix and README/plan. Preserve earlier commands, distinguish mock effects from real integrations, and state which checks actually ran on which platform. A planned feature or configured CI job is never described as a verified runtime control.
