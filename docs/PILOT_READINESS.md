# Supervised pilot readiness plan

Status: synthetic lab readiness work, not approval for a real deployment. Original phases 1–8 and comparison phases A–E have lab implementations. Passing local checks is not a production authorization. Real resources, identity provider, provider accounting and monitoring/recovery owners must be chosen and reviewed before any live pilot.

## Allowed task and inventory

The current authorized task is to review fixed synthetic documents, propose a bounded report, and optionally exchange the exact phase 8 facts/acknowledgment between two named agents. Resources are `documents/task`, `documents/reference`, `reports/draft`, the broker's run-bound `facts`/`ack` routes and disposable local fixtures. No shell, arbitrary HTTP/SQL, user document mounts, host socket, real permission changes or third-party publication is authorized. The comparison executable contains intentionally vulnerable synthetic fixtures and must never be part of a production worker or gateway deployment.

Trusted components are the supervisor/operator, grant/credential issuer, policy gateway, executor, external audit collector, independent evaluator and namespace guardian/relay. Workers, documents, model proposals and message text are untrusted. Keys belong only to the trusted component that needs them. Broker/evaluator authentication keys are separate. Only the broker public signature key is supplied to the evaluator. Runtime fixtures and credentials remain ignored by Git.

## Threat model and grants

Threats exercised in the lab include identity/scope substitution, prompt injection, forged supervisory messages, cross-run communication and shared-storage channels, approval substitution/replay, version races, lost acknowledgments, audit rewriting/outage, concurrent overspend, repeated failures and work continuing after a stop. Assume the trusted host, Docker engine and guardian are not compromised. A compromised signing authority can forge its own evidence; a signed transcript is not independent proof of an uncompromised host.

Grant only the exact task's resource IDs and allowed sender/recipient/topic tuples. Production identity must use a reviewed workload identity mechanism instead of the educational HMAC format. Require TLS outside the explicit loopback lab exception. Separate read, write, operator and evaluator credentials. Denied attempts consume admission quota. Retain bounded, non-root workers with private workspaces, no writable shared cache, no host ports or sockets, and externally enforced network denial.

## Numerical entry and exit criteria

These targets are set before a future live trial; they are not claims of measured production performance.

| Gate | Proposed criterion |
|---|---|
| Offline regression | 100% of executable security checks pass; zero unexpected effects |
| Synthetic comparisons | All 12 pairs show their unsafe failure and secure prevention with positive controls |
| Container bypass | Both original and collaboration worker topologies pass probes; no direct protected access or foreign workspace bytes |
| Evidence | Each consequential write has exact approval, idempotency/version binding and verified audit; unavailable audit causes zero new effects |
| Shadow stage | At least 100 representative reviewed tasks; zero live write authority; 100% of policy/evidence checks pass |
| Supervised stage | At most 10 tasks/day, one active worker/task, one reviewed write/task; expand only after separate review |
| Initial resource budget | At most 32 tool attempts, 4 KiB document response, 512-byte message content and 2 KiB message content budget per agent; task deadline 3 minutes |
| Real provider spending | Set a numeric monetary ceiling and independently reconcile actual usage before enabling any paid trial; synthetic quote units do not satisfy this gate |
| Containment | Target no new admitted effects after stop; cooperative cancellation observed within 2 seconds; owned process exit within 5 seconds; quarantine uncertain completed effects |
| Exit immediately | Any unauthorized read/write/message, missing required audit, quota overspend, identity/evaluator compromise, or inability to stop owned work |

## Review procedure and ownership

Before live entry, record the exact data classification, inventory/destinations, consent, grants, provider settings, retention limits and rollback behavior. Name a monitoring owner, an incident responder and an independent approver; these roles are deliberately unassigned until the user chooses a real pilot. Review the proposed report's full content, destination, expected version and expiry. Approval must not expand a grant. Require a passing current remote CI run and inspection of the corresponding container evidence before authorizing pilot entry.

No production service, credential, dataset, account or deployment has been selected by this lab. No shadow run has occurred. This document prepares the gate; it does not waive those decisions or enable broad autonomy.

## Incident and recovery procedure

Run `./scripts/Run-ResponseDrill.ps1` to exercise versioned writes, quotas, cancellation, revocation and owned-process termination. Run `./scripts/Run-IsolatedCollaborationLab.ps1` for independent worker/evaluator boundaries and `./scripts/Run-ComparisonLab.ps1 -Scenario all` for measured before/after effects.

On a suspected incident: stop admission, revoke run credentials, cancel active work, terminate only owned workers, preserve signed telemetry and inspect actual completed effects. A token revocation never undoes a committed publication. Reconcile a pending durable transition against its idempotency record and the independent signed audit head; do not retry with a new key or weaken audit. Restore service only after the independent approver reviews the root cause, evidence and new bounds. Volatile broker runs are discarded, not recovered; issue new keys/grants/challenges for a fresh lab.

Saved comparison summaries are safe synthetic presentation evidence. Raw runtime fixture directories can contain short-lived synthetic run/approval identifiers and should remain private/ignored. Publish only the explicitly selected redacted summaries, public verification keys and synthetic transcripts. Delete chosen evidence only after review and retention requirements are settled.
