# Secure Agent Lab — implementation plan

## Goal and scope

Build a .NET 10 proof of concept that makes agent authorization independent of model reasoning. Start with a deterministic worker and synthetic resources. Demonstrate scoped access, approval, identity expiry, quotas, audit evidence and containment before adding a model or real tools.

Repository: https://github.com/Tmham92/Secure-agent-lab

Read README.md and DEMO_GUIDE.md for the threat model, demonstration commands and evidence. The milestones below retain their original acceptance requirements.

## Current status as of 10 October 2026

| Original phase | Implementation and verification | Remaining scope |
|---|---|---|
| 1 Deterministic gateway | Implemented; core checks and offline demo passed locally and in Actions run 2 | Production integration is outside this synthetic phase |
| 2 Authenticated processes | Implemented; transport/grant checks passed locally and in Actions run 2 | Educational HMAC credentials are not a production identity provider |
| 3 Durable approval and audit | Implemented; restart, crash, replay and tampering checks passed locally and in Actions run 2 | Local synthetic effects, not arbitrary external exactly-once transactions |
| 4 Isolation and egress | Implemented; user supplied successful local two-worker output with two reads, zero publications and unchanged canary | Passing isolation CI on final fixes must be confirmed separately |
| 5 Scoped document adapter | Implemented; Windows and Linux document checks passed in Actions run 2 | Synthetic local files, not a production document service |
| 6 Optional model proposals | Implemented; full solution builds and offline model checks passed in Actions run 2 | Live provider calls remain unverified |
| 7 Bounded writes and response | Implemented for the synthetic lab; 13 response checks passed locally and in Actions run 2 | Provider accounting uses synthetic units; multi-host coordination and production external writes are not implemented |
| 8 Collaboration and independent verification | Synthetic broker/evaluator implemented; 18 local checks, two-service demo and isolated two-agent container demo passed locally | Broker is volatile and single-process; remote CI for these changes remains pending |

All eight phases now have synthetic lab implementations, with the limits above. The phase 8 isolated topology now also passes local bypass/workspace checks and independent scoring. Remote CI for these changes and the real production pilot gate remain unfinished; synthetic provider accounting and local coordination do not satisfy production integration requirements. No production deployment or broad autonomy has been enabled.

The before-and-after educational extension in [the consolidated handout](docs/Secure_Agent_Lab_Handout.md), phases A to E, is now implemented: 10 portable pairs plus 2 container pairs, all locally verified. A reviewable future pilot gate is prepared in [docs/PILOT_READINESS.md](docs/PILOT_READINESS.md); no real pilot is approved. The presenter handout is maintained in [docs/Secure_Agent_Lab_Handout.docx](docs/Secure_Agent_Lab_Handout.docx), with editable text alongside it. Extend DEMO_GUIDE.md and the handout when each comparison phase finishes.

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

Historical troubleshooting follows. The latest complete local rerun now passes both workers, deployment inspection and effect counts; the current remote CI result is still pending. See the current status matrix and guide for acceptance evidence.

CONNECT probe correction: the next local run verified relay startup/readiness and preceding bypass checks. Added the explicit synthetic Host authority required by .NET so CONNECT denial is measured at the relay rather than failing during client construction. Final two-worker/effect acceptance remains pending.

Confirmed startup correction: local relay logs showed a read-only-filesystem error creating the default FastCGI temp directory. Explicit FastCGI/uWSGI/SCGI temp paths now use the relay's existing bounded `/tmp` mount without relaxing isolation. Full runtime acceptance requires rerunning the container demo.

Relay follow-up: Actions run #2 passed Linux/Windows builds and check jobs; isolation failed on its first relay request. Added worker-origin readiness (backend 401 without credentials/effects), streamed/persisted worker output and safe relay/firewall diagnostics captured before cleanup, with failed-run CI artifact retention. Full isolation acceptance remains pending a rerun.

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

### Phase 8 implementation on 9 October 2026

The user selected a multi-agent demonstration. `SecureAgentLab.Collaboration` adds a separate opt-in broker and independent evaluator; the original gateway still denies `MessageAgent`. `Run-CollaborationLab.ps1` starts the two trusted services as separate owned processes. Researcher/writer HTTP clients use different grant-bound credentials, exchange exactly scoped synthetic facts and acknowledgment, and submit one answer. An evaluator-only random challenge binds a sealed RSA-PSS-signed transcript to one fresh evaluation. The evaluator checks exact task facts, sender/recipient/topic and ordered send/consume/submit effects, as well as its private expected answer.

Atomic per-agent attempt/UTF-8 quotas, queue/audit capacities, expiry, revoke, stop, immutable grants, strict request schemas and worker/operator separation are enforced. Workers cannot publish via cache/artifact/log APIs or access evaluator routes with broker credentials. Saved evidence excludes bearer credentials, signing keys and expected-answer configuration.

Verified locally: full Release build, 18/18 phase 8 checks, all existing check harnesses, and the two-service script. CI now includes phase 8 checks/demo on Linux and Windows but has not run for this change. Broker state is intentionally volatile and single-process; repeated sends create distinct messages, while each queued envelope is consumed once. There is no persistent message recovery or multi-replica delivery protocol. The deterministic clients are simulated workers in the trusted harness, not hostile OS processes. Existing phase 4 container evidence must not be presented as verification of this new collaboration topology. The later isolated collaboration implementation below supersedes this initial desktop-only validation limit.

### Remaining lab work completed

`Run-IsolatedCollaborationLab.ps1` now places researcher/writer in distinct non-root, readonly containers with private tmpfs workspaces, no host mounts/sockets and relay-only networking enforced by the trusted IPv4/IPv6 guardian. Both workers passed origin readiness, workspace/secret absence, direct broker/evaluator/metadata/host denial, alternate-channel denial and deployment inspection. The isolated task passed independent evaluation (2 delivered/consumed messages, 1 submission). The original `Run-IsolatedLab.ps1` was rerun successfully too. Saved evidence and cleanup are described in DEMO_GUIDE.md section 13.

Comparison phases A–E are implemented in the separate `SecureAgentLab.Comparisons` executable and disposable deployment. Ten portable pairs pass on Windows and Linux; isolation/containment pairs pass in Linux containers; the aggregate launcher validates all 12 and links their actual effects. New container CI jobs are configured but remote results are not yet verified. This does not enable unsafe mode in the normal gateway. Detailed commands and limitations are in the guide and the single consolidated handout.

## Pilot gate

Before real deployment, record the allowed task, resource inventory, threat model, permission grants, limits, review procedure, monitoring owner and recovery process. Start in shadow mode, then a small supervised pilot. Set numerical success and containment criteria before observing the results.

Each implementation milestone should be a reviewable pull request containing changes, security evidence and updated limitations. Do not broaden permissions because an agent asks or a test is inconvenient.

## First prompt to use on your desktop

> Read README.md and plan.md in Secure-agent-lab. Implement milestone 1 only: a .NET 10 solution with Core, Demo and dependency-free executable Checks projects. Create the host-owned default-deny gateway, synthetic read/publication tools, scoped sessions, one-time exact-action approvals, expiry, atomic quotas, revocation, global stop and hash-chained audit described in the plan. Use TimeProvider for deterministic expiry tests. Add the listed security checks and GitHub Actions. Run the build, checks and demo, fix failures, and update the README to distinguish implemented controls from simulations and future work. Keep all resources synthetic and do not add model keys or live targets. Prepare a feature-branch commit for review.

Then use the numbered prompts at the end of README.md to extend the project one milestone at a time.

## Maintainability upgrade R0–R8

The completed maintainability work and its validation are recorded in docs/REFACTORING_RESULTS.md. It preserves the original phase 1–8 capabilities and comparison scenarios. Coding standards, responsibility folders, individual types, smaller hosts and named checks are implemented; Windows/Linux regression, compatibility and container deployment verification passed locally and are recorded in the execution results. Remote CI for the working-tree changes remains pending.


Documentation review on 10 October: see docs/DEMO_REHEARSAL.md for the complete run order. Full Release build and 110 Windows checks passed again; prior Linux/container acceptance remains dated 9 October, with remote CI pending.

Namespace follow-up on 10 October: source and check namespaces now follow project folders; imports and qualified references are updated together. This supersedes retaining old C# namespaces. Demo commands and serialized contracts remain unchanged; migration verification is tracked in docs/REFACTORING_RESULTS.md.

Artifact retention follow-up: cases now overwrite a single latest artifact set and store attempt numbers in run.json, backed by persistent counters under artifacts/.runs. Cross-run fixtures are reset; state recovery inside each demo remains intact. Retention regression and repeat-run validation are recorded in docs/REFACTORING_RESULTS.md.
