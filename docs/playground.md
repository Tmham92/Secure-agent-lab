# Interactive Blazor playground

The playground is the beginner entry point. Existing CLI, authenticated gateway, durable approval, collaboration and container demonstrations remain advanced exercises. Run from the repository root:

```powershell
./scripts/Run-Playground.ps1
# Or:
dotnet run --project src/SecureAgentLab.Playground -c Release
```

Open http://127.0.0.1:5080. The application binds to loopback and rejects remote addresses and foreign Host headers. No account, database or model credentials are needed for scripted mode. Do not expose it as a shared service.

## First demonstration

1. Select **Indirect prompt injection** and **Scripted**.
2. Click **Compare both modes**.
3. Baseline reads the fake private fixture and records FAKE_SECRET_123 in the simulated attacker outbox.
4. Hardened denies the private read and attacker recipient. Both variants still produce a first-line summary.
5. Expand tool decisions to inspect arguments, reasons and returned content.
6. Select **Approval bypass**, compare, and review the exact legitimate follow-up message in the hardened result. Click **Approve and send this message** within the run's 60-second deadline. Only that recipient/body can execute, once.
7. Select **Runaway execution** and compare. Baseline completes the bounded script; hardened stops after six admitted tool attempts by default. It intentionally does not complete the summary after exhausting the budget.
8. **Reset my lab** clears only this circuit's two retained runs and their generated fixture files.

Edits affect the stored document and scripted first-line extraction. Scripted attack calls come from the selected scenario, not from parsing the prose. Use live mode to test how a model interprets edited text. Scripted mode is an enforcement test, not evidence that a model was compromised.

## Scenarios and reuse

| Playground scenario | Baseline effect | Hardened control | Existing deeper exercise |
|---|---|---|---|
| Normal ticket | Summary | Positive control remains possible | Core checks |
| Prompt injection | Fake secret reaches mock attacker outbox | Exact reads and recipient restrictions | Comparisons: injection |
| Unauthorized file access | Synthetic traversal alias returns fake private fixture | Exact resource mapping; existing FileDocumentReader pins directories and rejects links | Comparisons: files; DocumentChecks |
| Data exfiltration | Readable synthetic internal note reaches mock attacker outbox | Recipient allowlist | Comparisons: scope/actions |
| Approval bypass | Caller-approved message enters mock outbox | Independent exact-message approval; expiry and atomic single use | Comparisons: approval; DurableChecks |
| Runaway execution | Twelve reads, then summary | Denied calls count; hard limit and deadline | Comparisons: retry-limit/quota |

The baseline intentionally recognizes two private-file aliases inside its synthetic fixture. It does not concatenate arbitrary caller paths. This demonstrates the authorization failure without introducing a general host-file traversal primitive. Both modes reuse the existing safe file reader. Actual link/junction escape tests remain in DocumentChecks and the files comparison; the browser never creates links or invokes a shell.

## Boundaries and implementation

- The browser and document/model proposals are untrusted relative to server-side execution.
- LabRun.Execute validates every proposal immediately before effects. No prompt or browser flag grants authority.
- A scoped LabSession owns at most two independent runs per Blazor circuit. New tabs receive separate circuits; state is not shared or durable.
- Every run has a generated GUID directory beneath the OS temp directory's SecureAgentLab.Playground folder, with documents/, private/secret.txt, and output/summary.txt.
- Outboxes, traces and approvals remain in memory. No real email or arbitrary external request exists.
- Both modes enforce input/output bounds, a 60-second deadline, and at most 24 tool calls. Hardened defaults to six and accepts a configured limit of 2–16.
- Baseline sends arbitrary simulated recipients without approval. Hardened allows only support@example.test with server-issued exact-message approval. Model approved=true is explicitly rejected.
- Approval is bound to the run, which is owned by the circuit, expires after 30 seconds, and is consumed atomically with the send. The overall run deadline still applies.
- The UI displays summaries, arguments and outputs as escaped text. No raw HTML rendering.
- Reset/disposal deletes only fixed generated filenames and empty directories, without recursive traversal. A crash can leave synthetic fixtures in temp. Inspect and remove only confirmed application-owned GUID directories manually if needed; do not broadly delete the system temp directory.

## Optional live model

The host adapter uses the OpenAI Responses endpoint, bounded structured proposals, no redirects, no automatic retries, no provider tools, and store=false. Server authorization is identical in both execution modes. Configure a model available to your account; no model name is hardcoded.

Set OPENAI_API_KEY and LAB_OPENAI_MODEL through server-side environment variables, or use .NET user secrets:

```powershell
dotnet user-secrets set OPENAI_API_KEY '<your-key>' --project src/SecureAgentLab.Playground
dotnet user-secrets set LAB_OPENAI_MODEL '<your-model>' --project src/SecureAgentLab.Playground
$env:ASPNETCORE_ENVIRONMENT = 'Development'
./scripts/Run-Playground.ps1
```

User secrets load in Development. Never commit credentials. Live mode is disabled unless both values are configured. Only enter synthetic content: the editable ticket and bounded tool history go to the provider. The adapter makes at most eight requests per run, each with at most 1,500 output tokens, a 30-second request timeout and the overall 60-second deadline. A comparison can make up to sixteen requests. This is not a monetary spending cap; configure provider-side limits separately. No automatic live calls occur at startup, in tests, or on selecting live mode. Pressing Run/Compare initiates them.

The model proposes batches of up to three tools. Tool results feed subsequent proposals; writing a summary ends orchestration. A model may refuse or ignore the attack; this does not prove tool security. Live comparisons are independent nondeterministic runs, not identical model decisions. The UI does not estimate token costs. Provider failures are summarized without exposing credential-bearing requests or raw error bodies.

Protocol reference: https://developers.openai.com/api/docs/guides/structured-outputs

## Verification

```powershell
dotnet build SecureAgentLab.slnx -c Release
dotnet run --project tests/SecureAgentLab.PlaygroundChecks -c Release
dotnet run --project tests/SecureAgentLab.DocumentChecks -c Release
dotnet format SecureAgentLab.slnx --verify-no-changes --no-restore
```

The executable check project follows this repository's dependency-free check convention; use dotnet run, not just dotnet test. It covers dispatcher enforcement, approval integrity/races, cancellation, all scripted scenarios, circuit-owned run/reset isolation and a mocked provider exchange. CI includes it. No paid model calls are used for validation.

## Limitations

This is a local educational application, not a production security product. Baseline vulnerabilities are deliberately narrow synthetic behaviors. There is no model subprocess or OS/network sandbox around this web host. Local host administrators can modify files or process state. Hardened approval is an in-memory circuit capability, not production authentication. Reconnecting with a new circuit loses state; browser rendering is not an audit retention system. Trace output may contain the fake secret by design. Multi-host coordination, actual secret management and container isolation are separate existing exercises.

## Advanced scenarios — 13 October 2026

Open http://127.0.0.1:5080/advanced, or choose **Advanced scenarios** in the top navigation. The beginner selector still has six scenarios; the advanced selector adds fourteen exercises, grouped by execution type.

### Runnable baseline-versus-hardened comparisons (10)

Exact-content approval; cross-task resource scope; excessive tool authority; filesystem traversal/links/size; document injection; lost-acknowledgement idempotency across restart; audit tampering/truncation/outage; stale approved overwrite; concurrent quota overspend; retry amplification.

**Run selected exercise** executes the actual existing comparison. **Run all 10 comparisons** runs them sequentially and stops on cancellation or any non-passing result. Each result shows measured effects, legitimate positive controls, input/fingerprint, safeguard and captured runner output. PASS requires exit code zero and the comparison's actual baseline-failure/hardened-success assertions. Outputs are escaped text.

These are fixed reproducible experiments. Editable model prompts and live provider behavior remain in the basic playground; advanced runs do not make paid model calls.

### Runnable hardened check suites (2)

**Durable approval and crash recovery** runs the existing 20 durable checks. **Multi-agent identity and channel security** runs the existing 18 collaboration checks. They show captured output and exit status; no vulnerable baseline is invented for these suites. Tests create only their existing synthetic fixtures and local loopback services.

### Guided Docker exercises (2)

**Container filesystem and network isolation** and **Container stop and late-effect containment** show learning goals, expected results, prerequisites, saved-evidence locations and exact commands. Run these in a repository terminal with PowerShell and Docker in Linux-container mode:

```powershell
./scripts/Run-ContainerComparisons.ps1 -Scenario isolation
./scripts/Run-ContainerComparisons.ps1 -Scenario containment
```

The web host does not start Docker or claim these exercises have run. Their existing launcher owns network/container setup, verification, evidence and cleanup.

### Build, authority and artifacts

`./scripts/Run-Playground.ps1` now builds the complete solution first so advanced executables exist. When launching the Blazor project directly, first run `dotnet build SecureAgentLab.slnx -c Release`. Missing binaries produce **Build required**. Published standalone web output is not supported for this repository-based advanced runner.

The web host never references the deliberately vulnerable Comparisons assembly. It starts only three fixed .NET executables, with allowlisted argument arrays; user-selected IDs must match the catalog. No browser-provided command, path, arguments or environment variables are accepted. Child environment variables are cleared and rebuilt from an OS/runtime allowlist, excluding model keys and application credentials. A child process is not an OS sandbox and retains the host user's OS access.

Each run has a 90-second deadline and a combined stdout/stderr limit of 131,072 characters. Cancellation, timeout or overflow terminates the owned process tree and releases admission. Only one advanced execution is admitted per web host across browser circuits. An external CLI can still contend for the same artifact case; existing filesystem leases fail closed. Other browsers never receive another circuit's result list. At most fourteen bounded result snapshots are retained per page circuit.

The new fixed comparison CLI mode is:

```powershell
dotnet run --project src/SecureAgentLab.Comparisons -c Release -- --playground audit
```

It delegates to the existing comparison implementation, saves evidence under `artifacts/playground/advanced/<scenario>/latest`, and emits a structured result marker for the UI. Original `portable`, named scenarios and container role arguments remain supported. Durable and collaboration suites use their original artifact cases. Latest evidence is replaced on the next run of the same exercise; the browser retains a bounded result snapshot, not a permanent audit archive. **Clear displayed results** clears only the current page's display; it does not delete saved evidence. Cancellation can leave partial synthetic evidence; do not interpret it as a passing run. A later run uses the existing artifact manager's bounded cleanup and replacement.

Current implementation and validation evidence: [playground-verification.md](playground-verification.md). No new Docker run or remote CI execution is implied.
