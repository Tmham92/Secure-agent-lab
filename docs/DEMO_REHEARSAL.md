# Complete demo rehearsal

Documentation reviewed 12 October 2026 against the current launchers. This review does not establish a new full rehearsal or hosted CI result. Use this checklist for the run order and the single Secure_Agent_Lab_Handout.docx for explanations. DEMO_GUIDE.md contains detailed trust boundaries and recovery. All examples use synthetic data. The default model demo is offline and needs no API key.

## Browser learning path

Run `./scripts/Run-Playground.ps1`. At http://127.0.0.1:5080 compare injection modes and review exact-message approval. At /advanced run comparisons and inspect measured effects/positive controls. Durable/collaboration suites run from the UI; Docker exercises show commands/prerequisites. See [playground instructions](playground.md) and [verification](playground-verification.md). The infrastructure presentation below retains its original run order and handout.

## Prepare

Open PowerShell 7 and run from the repository root. Start Docker Desktop and wait for its Linux engine. Run each command separately; continue only when it completes successfully. First container builds may take several minutes.

```powershell
Set-Location C:\Users\tmham\source\repos\Secure-agent-lab
$PSVersionTable.PSVersion
dotnet --version
docker info --format '{{.OSType}}'
docker compose version
dotnet build SecureAgentLab.slnx -c Release
./scripts/Test-ProcessEnvironment.ps1
./scripts/Test-SourceLayout.ps1
```

Expect PowerShell 7, the SDK selected by global.json (10.0.400), Docker linux and a successful build. If scripts are disabled, set execution policy for this session only: `Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned`. If `pwsh` is missing, open the installed PowerShell 7 application or install PowerShell 7 before proceeding. Do not run the demo launchers concurrently: several share local ports.

## Run every secure demonstration

| Step | Command | Explain and verify |
|---|---|---|
| 1 Introduction | `dotnet run --project src/SecureAgentLab.Demo -c Release --no-build` | Agent proposals go through the trusted gateway; observe scope, approval and stop decisions |
| 2 Identity and task scope | `./scripts/Run-Lab.ps1` | Scoped read allowed; external request, messaging and permission changes denied; publication requires approval; zero publications |
| 3 Approval and recovery | `./scripts/Run-DurableLab.ps1` | Trusted exact-content approval; restart preserves state; retry does not duplicate the publication; signed audit evidence |
| 4 Environment restoration | `./scripts/Run-Lab.ps1` | Basic demo still succeeds after the durable demo in the same terminal |
| 5 Worker isolation | `./scripts/Run-IsolatedLab.ps1` | Both workers pass bypass probes and authorized proposals; final two reads, zero publications, protected canary unchanged |
| 6 Protected document reads | `./scripts/Run-DocumentLab.ps1` | Worker requests resource IDs; trusted adapter returns only catalogued pinned content with measured limits |
| 7 Untrusted model proposals | `./scripts/Run-ModelLab.ps1` | Fixed offline adversarial proposals cannot expand scope or expose host credentials; document contents remain data |
| 8 Writes and incident response | `./scripts/Run-ResponseDrill.ps1` | Version conflicts, three crash points, shared limits, timeout/revocation/quarantine and owned-process termination; all 13 checks must pass |
| 9 Authorized teamwork | `./scripts/Run-CollaborationLab.ps1` | Scoped broker exchange and independent evaluation; unauthorized methods fail verification |
| 10 Isolated teamwork | `./scripts/Run-IsolatedCollaborationLab.ps1` | Separate worker containers, workspace/network probes and independent scoring; require final completion |

These ten steps cover all eleven handout use cases; some launchers demonstrate several controls. Read the final completion/effect assertions, not just an intermediate PASS. A script exception or nonzero executable exit means the step failed. Never waive a failed probe to continue presenting a successful result.

## Run every unsafe versus secure comparison

```powershell
./scripts/Run-ComparisonLab.ps1 -Scenario all
```

This executes all twelve pairs, including container isolation and containment. Expect `Comparison overview (12 pairs)` and `Selected comparison suite completed: all`. Open the printed summary.md to inspect measured effects and positive controls.

For an individual explanation, rerun with one selector: approval, scope, actions, files, injection, retry, audit, version, quota, retry-limit, isolation or containment. `portable` runs the ten pairs that do not need Docker. A vulnerable collaboration pair is not implemented; teamwork is demonstrated by the two secure collaboration launchers.

Point out the actual local fake-secret capture: unsafe one, secure zero. For containment, unsafe late write one, secure zero; effects completed before stop remain preserved. These are bounded synthetic examples, not live attacks.

## Evidence and cleanup

The launchers print their evidence paths. Basic gateway logs are under artifacts/gateway/latest; durable evidence under artifacts/durable-demo; document fixtures under artifacts/document-demo; runtime drill evidence under artifacts/phase7; collaboration under artifacts/phase8 and artifacts/phase8-isolated; isolation under artifacts/isolation. Comparison suite summaries and linked per-pair evidence are under artifacts/comparisons.

Launchers clean up their owned processes and Compose deployments in finally blocks while retaining evidence. Do not remove unrelated Docker resources. Copy any failure evidence you need outside artifacts before rerunning that case: the next attempt replaces its latest set. Raw state may contain generated lab credentials or keys; keep it private and ignored by Git. Share only reviewed redacted summaries. Do not delete durable state to make a recovery failure disappear.

If Docker reports a missing Linux-engine pipe, start Docker Desktop and wait for `docker info` to succeed. If a gateway fails, inspect the exact stdout/stderr paths printed by that launcher; a reported HTTP 400 is a rejected request, not a readiness timeout. For isolation failures inspect worker and diagnostics logs in that run's directory. Avoid changing grants, firewall rules or approval validation to force a pass.

## Verification status

The 17-project Release build and all 110 Windows checks passed on 10 October 2026, along with formatting, layout/syntax and environment checks. The complete desktop sequence, both isolated demos, all twelve comparison pairs, 112 Linux checks and original-state compatibility passed during refactoring acceptance on 9 October. This checklist has been checked against current scripts; a new complete presenter run has not been performed on 10 October. Record your run results separately. Remote CI for the uncommitted refactoring remains pending; live provider calls and a production pilot remain outside this rehearsal.

Namespace follow-up on 10 October: rebuild all projects before this sequence. Namespaces now mirror folders and caller imports have changed; the demo commands and expected effects are unchanged. See docs/REFACTORING_RESULTS.md for migration-specific validation.


## Latest artifact retention

Each case reuses artifacts/<case>/latest. Read run.json for RunNumber, StartedUtc and FinishedUtc. The number counts attempts and starts at one under the new policy; FinishedUtc means the run released its lease, not that every assertion passed. Counters under artifacts/.runs persist across replacements. Do not delete them if you want numbering to continue. A second active run of the same case is rejected.

Comparison evidence uses artifacts/comparisons/portable/latest, artifacts/comparisons/container/latest and artifacts/comparisons/suite/latest. The suite index links the current sets. An individual portable comparison replaces the previous portable selection; rerun all for a complete twelve-pair overview. Fixture subdirectories use stable case numbers and the entire case set is reset before its next attempt. Recovery keeps multiple state/audit files within a run; it does not reuse old state across demonstrations.

Only reviewed summaries should be shared. This overwrite policy is for disposable synthetic lab data, not production audit retention. scripts/Clear-LegacyArtifacts.ps1 removes recognized older generated histories while preserving latest sets, counters and unknown files.
## Descriptive demo output and artifact guides

All ten Run-*.ps1 demo entry points now introduce the purpose, print numbered stages and end with an explicit PASS or FAILED result. A denied attack is an expected security success; gateway decisions include plain-language explanations. Container build details go to a fixed build.log; a failed build shows its final diagnostics and the full log path. Run-Lab and Run-DurableLab also retain worker-decisions.log and effects.json, which records operator-observed tool-effect counts.

Commands and prerequisites remain unchanged: build Release first; use PowerShell 7 and Linux Docker for the container demos. The offline model demo requires no API key. Existing actor boundaries, synthetic effects and cleanup remain unchanged. Presentation uses the host stream so machine-readable comparison return objects remain usable by the suite script.

Each demo writes artifacts/<case>/latest/artifact-index.md with the run number, completion result and relative links to existing files, describing their contents and significance. Document/model wrappers link their companion gateway guide; the comparison suite links the selected evidence sets. These indexes and logs are overwritten with the next run, under the existing retention policy. Terminal links use OSC 8 when virtual-terminal support is available; otherwise full local paths remain visible. Open the Markdown guide when the terminal does not support clickable links.

The index lists filenames, not file contents. Private runtime material is identified for review before sharing; file descriptions are guidance based on names, not a new verification of contents. Lifecycle metadata and fixture existence alone do not establish a security verdict. Failed invocations show FAILED and can retain partial evidence; original exceptions still propagate. Refer to the existing per-demo sections for threat, actors, exact commands, expected effects and limitations. No live model calls or production changes are part of presenter validation.


## Explain the printed code flow

Before each scenario, read its How this demo connects to the code section aloud. Identify the coordinator, the untrusted proposal or agent, the enforcing C# component or container boundary, and the measured effect. Document/model demos include the gateway in their own flow, share one continuous stage sequence with it and show one final result. The gateway evidence guide is generated quietly and linked from the final summary. Distinguish offline fixture input from explicit live model calls, local collaboration services from isolated containers, and a gateway denial from a measured direct-network bypass. For the per-script mapping see [the code-flow guide](../DEMO_GUIDE.md#code-flows-for-every-demo-script) and the consolidated handout. Rebuild host-run Release DLLs after C# changes.

At completion, open artifact-index.md in the printed latest directory; check its run number, result and selected companion guides. Check measured effects, not only individual PASS lines, and save needed evidence before the next overwrite. Local verification and its dates are recorded in REFACTORING_RESULTS.md.
