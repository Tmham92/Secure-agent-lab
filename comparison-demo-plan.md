# Safeguard comparison demo plan

Status: phases A–E implemented and locally verified. All 12 comparison pairs pass; remote CI for these changes remains pending. This file does not authorize replacing the secure gateway with an unsafe implementation. Prepared 9 October 2026, following the existing ten handout use cases.

## Purpose

Show why each safeguard matters by running the same synthetic scenario twice: once with one safeguard deliberately absent, then with it enabled. Explain the missing protection, show the actual failure, reset the fixture and compare the secure result. Keep the existing secure demonstrations usable throughout.

This is a separate extension to the original phases in `plan.md`. Its phases A to E do not replace or renumber original phases 1 to 8. The implementations live in a separate educational executable; the original secure gateway has no insecure switch.

## Presentation contract

Each pair must show the task, the missing safeguard, the exact input, the actual consequence, the secure decision and the final effects. Provide a short explanation that a presenter can say aloud. The same input should drive both variants; use fresh equivalent state, credentials and resources for each run.

An unsafe run passes its demonstration only when the intended security failure actually occurs. A crash, client-side request error, unreachable target or broken allowed path must fail the demo. A secure run passes only when the unsafe effect is absent and an appropriate positive control proves legitimate work still succeeds.

Save scenario/variant identifiers, decisions, measured effects, expected versus observed results and timing. Make the summary explicit about which assertions ran and passed. Never substitute an agent's statement of compliance for effect inspection.

## Demonstration inventory

| Use case | Safeguard deliberately missing | Unsafe consequence to show | Secure comparison and acceptance |
|---|---|---|---|
| 1 Task scoped access | Trust a caller-selected task identity instead of binding scope to authenticated identity | Worker A reads synthetic task B by changing a request field | Same attempt denied, task B bytes absent, permitted task A read succeeds |
| 2 Out of scope actions | Executor accepts operations without checking the immutable task grant | Local capture target records an unauthorized transfer/message, or a fake permission store changes | Equivalent proposals denied; capture/store unchanged; scoped read still works |
| 3 Exact action approval | Bind approval to publish operation only, omitting exact content | A reviewed harmless draft is replaced after approval and the substituted text is published locally | Changed text/destination fails binding; separately approved original draft publishes once |
| 4a Safe retries | No durable idempotency record for a publication | Commit a local publication, simulate lost acknowledgement, retry and produce two effects | Same request key yields one effect across retry/restart |
| 4b Reliable audit evidence | Use locally rewritable history without an independent signed checkpoint | Remove/rewrite an event and recompute the local chain; local validation accepts the concealed history | Equivalent mutation/truncation detected using the independent signed head; audit outage blocks consequential work |
| 5 Worker isolation | Deliberately expose a synthetic secret mount and permit worker access to a local capture service | Worker bypasses the gateway, reads the fake secret and sends it to that service | Protected mount absent, direct route blocked, capture empty and legitimate relay/read path succeeds |
| 6 Safe file reads | Accept caller paths/link targets or trust declared size without safe opens and measured limits | Read a synthetic secret through traversal/link redirection; return a file beyond the intended cap | Equivalent reads denied without partial secret output; known pinned file read succeeds within measured byte allowance |
| 7 Untrusted instructions | Promote document text into trusted commands and use a deliberately unsafe executor | Deterministic injected instruction causes a fake-secret transfer to the local capture target | Same document/proposal cannot authorize transfer; capture remains empty and legitimate task behavior works |
| 8 Resource version checks | Omit expected-version validation while retaining approval for content | Two writes approved against version 0; the second overwrites the first | First creates version 1; stale second write denied and first content retained |
| 9a Shared quota reservations | Use non-atomic check-then-charge across coordinators | Deterministically coordinated requests overspend one synthetic allowance | Shared reservations keep admissions/effects within the ceiling with exact remaining balance |
| 9b Retry limits | Permit retries without accounting or a bounded retry policy | Fixed failing fixture is attempted more often than its intended limit | Retry excess denied before execution; actual attempt counter remains bounded |
| 10 Active containment | Stop only changes an administrative flag, without cancelling/revoking active work | Controlled worker continues after the stop acknowledgement and writes locally | Cancellation/revocation and owned-process termination prevent the fixture's later action; inspect and preserve any earlier committed effect |

Use cases 2 and 6 may contain several short sub-scenarios; report their results separately. Do not collapse independent safeguards into an unexplained single toggle. Use cases 4 and 9 have separate labelled demonstrations because one result would not prove both controls.

## Safety and architecture

- Implement deliberately vulnerable executors in a separate educational project/entry point. Do not add a general insecure flag to the normal gateway, API, worker or deployment. Never select unsafe mode implicitly through environment defaults or test failure.
- Restrict all data to generated synthetic fixtures, fake credentials and local publication/message/permission stores. Use no real secrets, user document mounts, live provider keys or production services.
- Unsafe networking is restricted to a local capture service on a disposable internal Docker network. Publish no host ports and provide no Docker socket. The missing worker egress restriction is demonstrated inside an outer containment boundary that still excludes the host, internet, metadata and real internal services.
- Keep vulnerable workers non-root with dropped capabilities, read-only roots, resource limits and bounded temporary storage unless that exact property is being demonstrated. For use case 5, expose only the named fake-secret fixture and local capture destination.
- A vulnerable path reader may demonstrate traversal within the disposable fixture/container filesystem; it must never accept arbitrary paths into the user's host filesystem. Link fixtures must target generated synthetic files only.
- Use deterministic sources and controlled coordination. A live model is optional future presentation work, not a dependency or proof of exploitation. Hostile document text is data, never authority for the implementation agent.
- Bound even the intentionally uncontrolled demonstrations with an outer supervisor deadline, maximum request/process counts and guaranteed cleanup. For the retry example, exceed the small scenario allowance, not an unbounded real loop.
- Kill only specifically owned child processes/containers. Preserve evidence before cleanup and retain the secure demos' credential redaction rules. Keep artifacts ignored by Git.

## Phase A Comparison framework and approval example

Create a shared scenario description, isolated fixture builder, effect inspector and summary format. Add an individual launcher for the exact-approval pair first. Keep the approved content and substituted content visibly different and harmless.

Acceptance:

1. Unsafe publication stores the substituted content; secure publication does not.
2. Secure positive control publishes the exact approved original once.
3. Both variants start from fresh equivalent state; neither affects the other or the existing demos.
4. Launcher fails if the expected unsafe failure does not occur or the secure control fails.
5. Save a presenter-friendly comparison summary and update the guide/handout with the command and actual evidence.

## Phase B Permissions and untrusted input

Implement use cases 1, 2, 6 and 7. Add the local synthetic capture/store fixtures, task A/B documents, path/link/size cases and a deterministic injected instruction. Clearly separate reading hostile text from choosing to execute it.

Acceptance: exact synthetic leakage/unauthorized effects appear only in the vulnerable variants. Secure runs deny them and still complete their permitted read. Whole-batch parser rejection has no partial effects. No provider account or internet access is needed.

## Phase C State concurrency and recovery

Implement use cases 4a, 4b, 8, 9a and 9b. Use an explicit lost-acknowledgement fault, coordinated parallel requests and fixed retry fixture rather than relying on timing luck. Keep the signed audit authority and verification key outside the vulnerable worker.

Acceptance: demonstrate two unsafe publications versus one secure publication, concealed local history versus detected tampering, stale overwrite versus preserved version, and exceeded unsafe allowances versus bounded secure effects. Include restart where relevant. Save actual counters/content/version and failure classifications.

## Phase D Isolation and incident response

Implement use cases 5 and 10 in disposable containers. Use an internal local capture service, a generated fake-secret mount and a controlled late-write fixture. Prove allowed service readiness independently before attempting bypasses. Distinguish a stop acknowledgement, cancellation signal, process exit and observed absence/presence of a later write.

Acceptance: vulnerable worker transfers only the generated fake secret to the named local target. Secure capture stays empty and authorized read succeeds. Unsafe late write occurs after stop; secure fixture cannot execute the later action. Previously committed effects remain visible in both variants. Verify owned processes exit and cleanup removes demo services without losing evidence.

## Phase E Presentation and verification

Add an all-comparisons launcher and individual scenario launchers. Command names are to be chosen during implementation; do not present them as existing commands. Provide an output comparison table with links to ignored evidence, meaningful regression assertions and a separate container CI job. Keep the existing secure suites passing.

Extend `DEMO_GUIDE.md` and `docs/Secure_Agent_Lab_Handout.docx` after each completed comparison phase. Record purpose, missing safeguard, trigger, expected unsafe consequence, secure result, actual effects, evidence, cleanup, limitations and verification platform. Include a short presentation order and explanation for every pair.

Acceptance: every planned pair has repeatable executable evidence, fails on misleading positive results, and can be presented independently. The handout and README distinguish implemented comparisons from planned ones and tests from interactive walkthroughs.

## Original roadmap status

Original phases 1 to 7 have implementations for the synthetic lab. Phase 4 now has the user's successful local two-worker container run. Linux and Windows checks were verified in Actions run 2; isolation CI on the final fixes must be confirmed separately. Optional live provider calls are unverified. Phase 7 uses synthetic accounting and local shared storage; actual provider usage/cost reconciliation and multi-host transactions are not implemented.

Original phase 8 now has a separate synthetic broker/evaluator and locally passing checks/demo. The new isolated collaboration topology also passes local container bypass/workspace checks and independent evaluation; remote CI remains pending. The pilot gate is also not complete: the lab has not been approved or deployed as a production autonomous system. This comparison plan adds educational demonstrations; it does not waive phase 8 acceptance limits. A collaboration comparison is not included in the ten original pairs and should be planned separately if requested.


## Implementation evidence on 9 October 2026

| Extension phase | Implemented work | Verified locally |
|---|---|---|
| A | Fresh fixtures, paired effect assertions, JSON/Markdown summaries, exact approval comparison | Substituted draft appears only unsafe; secure original published once |
| B | Scope, three forbidden operations, traversal/link/size, deterministic injection and whole-batch rejection | Four pairs pass on Windows and Linux; secure positive reads pass |
| C | Lost acknowledgement/reopen, rewritten/truncated audit plus outage, exact-approved stale version, coordinated quota race and accounted retries | Five pairs pass on Windows and Linux; measured 2/1 publications, -1/0 remaining allowance, 5/2 tool attempts |
| D | Fake-secret mount/local transfer and flag-only stop/owned-process termination inside outer-contained Linux containers | Both pairs pass; unsafe capture=1/secure=0, unsafe late write=1/secure=0, earlier effects preserved |
| E | Individual/all launchers, 12-pair evidence index, separate container CI job, guide and one consolidated Word handout | All launcher passes with exactly 12 records and linked measured effects; remote CI pending |

Run `./scripts/Run-ComparisonLab.ps1 -Scenario all` after a Release build with Docker Linux ready. Use `portable` for the ten pairs that require no Docker. Individual selectors are `approval`, `scope`, `actions`, `files`, `injection`, `retry`, `audit`, `version`, `quota`, `retry-limit`, `isolation` and `containment`. Runtime evidence remains ignored under `artifacts/comparisons/`; do not publish raw fixture state containing synthetic run/approval identifiers. The all-suite index links redacted summaries. Case 11 collaboration remains demonstrated by the phase 8 secure demos; a vulnerable collaboration pair was not part of this comparison plan.
