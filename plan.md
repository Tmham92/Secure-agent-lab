# Secure Agent Lab — implementation plan

## Goal and scope

Build a .NET 10 proof of concept that makes agent authorization independent of model reasoning. Start with a deterministic worker and synthetic resources. Demonstrate scoped access, approval, identity expiry, quotas, audit evidence and containment before adding a model or real tools.

Repository: https://github.com/Tmham92/Secure-agent-lab

Read README.md for the primary incident sources, threat model, security measures and extension prompts. This plan defines implementation tasks and evidence required to finish them. Milestone 1 is implemented in this repository and passes the local Release build, 17 executable checks and deterministic demo. The implementation is locally verified in the desktop checkout; Git metadata permissions currently block commit and publication. Milestone 1 acceptance still requires passing GitHub Actions on the published commit. Milestone 2 now has explicit contracts, immutable grants, a separate worker/API, host-issued signed credentials and HTTP security checks. Milestone 3 is implemented locally with durable atomic synthetic publication, exact-content approvals, an external signed audit collector, and 20 passing durable checks. Git staging/publication remains blocked by the sandbox. Milestone 4 now has a Linux-container isolation deployment and independent bypass probes; runtime acceptance is pending because the local Docker Engine is unavailable. Milestone 5 is now implemented with a bounded, pinned local synthetic document adapter, safe handle-relative file opens, 14 passing Windows checks and a file-backed process demo. Linux-specific file checks and container integration remain pending runtime/CI verification. Milestone 6 now has an optional host-side Responses adapter, a strictly validated proposal source, an offline adversarial demo and model checks. No paid API call was made. Full SDK restore for the new projects remains blocked by NuGet configuration access; direct .NET compiler checks and the existing-project API/worker builds provide local evidence. Milestone 7 now has an opt-in versioned synthetic report store, durable shared runtime reservations and a response drill with 13 passing local checks. Token/cost limits in HTTP admission are synthetic units; production provider accounting, distributed coordination and Docker/remote CI acceptance remain pending. Milestone 8 remains conditional and planned. DEMO_GUIDE.md documents every demonstration and must be extended after each finished phase; the tasks below retain their original acceptance requirements.

## Desktop setup and first commit

Prerequisites: Git, the .NET 10 SDK, and your normal authenticated GitHub workflow. Visual Studio, Rider or VS Code can be used.

1. Clone the repository on your desktop.
2. Place README.md and plan.md in its root.
3. Confirm `dotnet --info` shows the .NET 10 SDK.
4. Commit the documentation, then implement milestone 1 on a feature branch.

```bash
git clone https://github.com/Tmham92/Secure-agent-lab.git
cd Secure-agent-lab
# Copy the downloaded README.md and plan.md into this directory.
git add README.md plan.md
git commit -m "Add secure agent lab background and implementation plan"
git push -u origin main
git switch -c feature/initial-security-lab
```

If your GitHub default branch is protected, use a branch and pull request for the documentation too. These commands assume the initial repository is empty and the default branch is main; inspect your checkout before running them.

## Milestone 1: offline deterministic lab

### Project structure

Create a solution named `SecureAgentLab.slnx` with:

| Project | Kind | Responsibility |
|---|---|---|
| `src/SecureAgentLab.Core` | Class library | Policy gateway, sessions, approvals, mock executor, audit |
| `src/SecureAgentLab.Demo` | Console | Host-controlled scenario walkthrough |
| `tests/SecureAgentLab.Checks` | Console | Dependency-free security checks, nonzero exit on failure |

Use shared `Directory.Build.props` with net10.0, nullable reference types, implicit usings and warnings as errors. Ignore bin, obj, IDE state, .env and generated artifacts. Do not add actual credentials or live targets.

### Domain contracts

- `Operation`: ReadDocument, PublishReport, ExternalRequest, MessageAgent, ChangePermissions.
- `Proposal`: operation, exact resource identifier, optional estimated byte count. The estimate must never authorize actual transfer.
- `Outcome`: Allowed, Denied, ApprovalRequired.
- `Decision`: outcome, stable reason and optional synthetic result.
- `AuditEntry`: sequence, UTC time, run identity, proposal metadata, outcome, reason, previous hash and current hash.
- Session state: opaque host-issued run ID, expiry, remaining calls, remaining response bytes and revoked flag.
- Approval state: opaque ticket, exact session and proposal binding, expiry and consumed status.

Use TimeProvider for deterministic clocks. Keep issuance, approval, revocation and shutdown under trusted host control. The deterministic worker may submit proposals only. Sharing a process is a simulation, not protection against hostile plugins.

### Gateway behavior

1. Serialize policy checks and mock execution, including quota updates, so concurrent calls cannot overspend.
2. Deny if stopped, identity unknown, revoked or expired.
3. Deny when the call budget is exhausted. Charge each authenticated active attempt, including denied attempts.
4. Reject malformed proposals and unknown enum values.
5. Permit ReadDocument only for the exact identifier `documents/task`. Return fixed synthetic content and charge its measured UTF-8 bytes.
6. Permit PublishReport only for `reports/draft`, representing a fixed immutable mock draft. Without a ticket return ApprovalRequired.
7. Validate approval binding, expiry and one-time consumption before mock publication. Approval cannot expand task scope.
8. Deny external requests, agent messaging, permission changes and every other operation/resource combination.
9. Record every execution decision. Provide an audit snapshot without exposing mutable internal collections.
10. Support per-session revocation and global stop; global stop invalidates outstanding approvals and prevents new execution.

For this milestone, publication returns a fixed message and performs no external write. No filesystem paths, URLs or arbitrary commands are interpreted by the executor.

### Audit implementation and honest limits

Hash each structured event, including its predecessor hash, using SHA-256 and deterministic serialization. Verify event sequence, predecessor link and event hash. Test mutation detection.

Do not call this immutable logging. The process owner can rewrite the chain, and a valid prefix cannot reveal tail truncation without a trusted external checkpoint. State loss, memory growth and missing control-plane audit events must be documented until later milestones address them.

### Demo scenarios

Show an allowed read, rejected external request, rejected shared-cache message, rejected permission change, publication awaiting approval, publication after host approval, replay rejection and rejection after global stop. Print outcomes and reasons, followed by audit verification. All resources are synthetic; an external-looking string must never trigger HTTP.

### Required security checks

| Test | Required evidence |
|---|---|
| Unknown identity | No tool executes |
| Expired or revoked identity | Later requests denied |
| Exact authorized read | Fixed synthetic content returned |
| Traversal or other resource | Denied before execution |
| Unknown operation and malformed input | Fail closed |
| External request, messaging, permissions change | Denied |
| Publication without approval | ApprovalRequired, no side effect |
| Forged, expired or replayed approval | Denied |
| Approval used by another session | Denied |
| Approval used with changed proposal | Denied |
| Response-byte budget | Actual output size enforced, even if estimate is zero |
| Denied attempts and call quota | Attempts consume quota |
| Concurrent requests | Exactly the permitted number executes |
| Emergency stop | Subsequent requests denied |
| Audit event mutation | Verification fails |

Use a fake TimeProvider rather than sleeping. Assert actual mock effects where relevant. Make every failure terminate the harness with a nonzero exit code.

### CI and acceptance

Add GitHub Actions with read-only repository permissions, .NET 10 setup, Release build, executable checks and demo. The console harness is run with `dotnet run`; `dotnet test` will not discover it.

```bash
dotnet build SecureAgentLab.slnx --configuration Release
dotnet run --project tests/SecureAgentLab.Checks --configuration Release --no-build
dotnet run --project src/SecureAgentLab.Demo --configuration Release --no-build
```

Milestone 1 is complete only when these commands pass on your desktop and CI, the demo produces the expected outcomes, and README.md accurately describes the implemented behavior and limitations.

## Milestone 2: real trust boundary

Create a separate worker and ASP.NET Core gateway. Define explicit interfaces for proposal source, policy evaluation, executor, audit, approval and run-budget storage. Introduce immutable task grants and startup policy validation.

Authenticate workloads using short-lived credentials issued outside the worker. Validate issuer, audience, expiry and grant binding. Derive identity from the authenticated caller, not JSON fields. Separate operator APIs from worker APIs.

Acceptance: forged identity, wrong audience, expired token, unauthorized grant and worker calls to administrative endpoints fail before tool execution. Document the trusted computing base and direct-access paths that remain open until network enforcement is added.

## Milestone 3: durable approval and audit

Store sessions and approvals durably with atomic consumption. Bind publication approval to exact content hash, destination, run identity, policy version and expiry. Make side effects idempotent and define crash recovery across approval consumption and execution.

Log grant issuance, approval, revocation, stop and execution decisions to an external sink. Add independently stored signed checkpoints and secret redaction. Fail closed for consequential actions when required audit or authorization dependencies are unavailable.

Acceptance: concurrent redemption, changed content, policy updates, replay, restart, sink outage, rewritten history and truncation are covered by meaningful tests.

## Milestone 4: isolation and egress

9 October 2026 CI follow-up: corrected the DNS probe to accept explicit socket access denial during send/receive and apply cancellation to send. Worker Release build passes locally. Supplied runner evidence covers startup positive controls only; full container acceptance awaits a passing rerun.

Deploy the worker separately with private per-run storage, non-root identity, read-only root filesystem, dropped capabilities and CPU/memory limits. Do not mount host sockets, broad secrets or writable shared caches. Restore dependencies separately from runtime.

Enforce default-deny networking outside the worker. Protected tools are reachable only through the gateway/executor. Restrict model-provider traffic and broker egress. Cover redirects, DNS rebinding, metadata endpoints, private ranges, IPv4/IPv6 and indirect exfiltration via permitted services.

Acceptance: independent bypass tests show the worker cannot directly reach protected resources, another run's storage, operator APIs or unapproved destinations. A rejected API call alone is not sufficient evidence.

## Milestone 5: one read-only integration

Add a narrow document-read adapter backed by synthetic test data first. Keep target credentials in the executor, outside worker storage and model inputs. Enforce resource scope, canonicalization, symlink safety and measured response limits.

Acceptance: cross-task reads, traversal, oversized responses, hostile tool outputs, credential exposure and executor failures are tested. No generic shell, arbitrary SQL or unrestricted HTTP tool is introduced.

## Milestone 6: optional model adapter

Keep the deterministic proposal source. Add an optional model adapter with strict schema validation and approved secret configuration. Treat all documents, model outputs and tool responses as untrusted data.

Use synthetic prompt-injection fixtures requesting secret reads, exfiltration, messaging, elevated privileges and forged approval. Assertions inspect gateway results and tool effects, not the model's claims of compliance.

Acceptance: identical authorization guarantees hold regardless of what the model proposes. Tests remain runnable without a paid model account; optional model integration tests are clearly separated.

## Milestone 7: bounded writes and response drill

Introduce one supervised write tool with exact-content approval, idempotency and resource version checks. Add run deadlines, active cancellation, monetary/token budgets, retry and fan-out limits, and transactional quota storage for multiple gateway replicas.

Exercise containment: stop active work, revoke credentials, terminate workers, preserve telemetry and inspect completed effects. Record which actions are reversible and how recovery works.

Acceptance: parallel overspend, hanging tools, retries after denial, cancellation, crash recovery and shutdown have measured outcomes. A completed irreversible effect is never described as undone by token revocation.

## Milestone 8: collaboration and independent verification

Only if the use case requires multiple agents, add an authenticated message broker with explicit sender/recipient/topic grants, quotas and audit. Keep workspaces isolated and test shared caches, artifact stores and logging as possible indirect channels.

Separate the evaluator's credentials, answers and scoring system from agents. Validate authorized methods as well as final output.

Acceptance: cross-run access, unauthorized messages, forged supervisory messages and evaluation tampering fail. No attack reproduction against real third-party infrastructure is needed.

## Pilot gate

Before real deployment, record the allowed task, resource inventory, threat model, permission grants, limits, review procedure, monitoring owner and recovery process. Start in shadow mode, then a small supervised pilot. Set numerical success and containment criteria before observing the results.

Each implementation milestone should be a reviewable pull request containing changes, security evidence and updated limitations. Do not broaden permissions because an agent asks or a test is inconvenient.

## First prompt to use on your desktop

> Read README.md and plan.md in Secure-agent-lab. Implement milestone 1 only: a .NET 10 solution with Core, Demo and dependency-free executable Checks projects. Create the host-owned default-deny gateway, synthetic read/publication tools, scoped sessions, one-time exact-action approvals, expiry, atomic quotas, revocation, global stop and hash-chained audit described in the plan. Use TimeProvider for deterministic expiry tests. Add the listed security checks and GitHub Actions. Run the build, checks and demo, fix failures, and update the README to distinguish implemented controls from simulations and future work. Keep all resources synthetic and do not add model keys or live targets. Prepare a feature-branch commit for review.

Then use the numbered prompts at the end of README.md to extend the project one milestone at a time.
