# Secure Agent Lab

A local C# / .NET 10 learning lab for AI-agent security. Compare deliberately vulnerable behavior with controls enforced outside the model, then inspect actual tool decisions and synthetic effects.

## Start the playground

Prerequisites: the SDK selected by `global.json`, PowerShell 7, and a browser. From the repository root:

```powershell
./scripts/Run-Playground.ps1
```

The launcher builds the solution and starts Blazor at [http://127.0.0.1:5080](http://127.0.0.1:5080). Stop it with Ctrl+C. Scripted exercises need no API key. Docker is needed only for container labs.

| Page | Exercises |
|---|---|
| [Basic playground](http://127.0.0.1:5080/) | Six scenarios: normal ticket, injection, unauthorized file access, exfiltration, forged approval and runaway execution. Edit documents, compare modes, inspect traces and approve exact simulated messages. |
| [Advanced scenarios](http://127.0.0.1:5080/advanced) | Ten real baseline-versus-hardened comparisons, two runnable durable/collaboration check suites and two guided Docker exercises. |

Start with **Indirect prompt injection → Compare both modes**. The vulnerable baseline captures a fake secret in a simulated outbox; the hardened run blocks the transfer while retaining the legitimate summary.

[Playground instructions](docs/playground.md) cover every scenario, optional live-model setup, execution limits, evidence and cleanup. Live mode can incur provider charges and remains unverified against a real account.

## Architecture

The agent proposes work; the host validates scope, approval and budgets before tools execute. Basic runs own isolated synthetic files and local-only outboxes. Advanced comparisons run fixed child executables; vulnerable code is not linked into Blazor. A child process alone does not provide OS isolation.

Original gateway, durable audit/recovery, document, collaboration and container exercises remain available. See [project responsibilities and transaction ownership](docs/ARCHITECTURE.md).

## Checks

Checks are executable harnesses; `dotnet test` alone does not discover them.

```powershell
dotnet build SecureAgentLab.slnx -c Release
dotnet run --project tests/SecureAgentLab.PlaygroundChecks -c Release --no-build
dotnet format SecureAgentLab.slnx --verify-no-changes --no-restore
./scripts/Test-SourceLayout.ps1
```

The complete matrix has 133 checks on Windows (110 original + 23 playground), or 135 on Linux with two extra document probes. Full commands are in [coding standards](docs/CODING_STANDARDS.md). [Playground verification](docs/playground-verification.md) and [infrastructure history](docs/REFACTORING_RESULTS.md) distinguish local results from unverified remote state.

## Documentation map

| Document | Purpose |
|---|---|
| [Playground guide](docs/playground.md) | Basic/advanced UI, provider configuration, limits and cleanup |
| [Complete demo guide](DEMO_GUIDE.md) | CLI use cases, actors, commands, expected effects and evidence |
| [Presenter rehearsal](docs/DEMO_REHEARSAL.md) | Run order and acceptance checklist |
| [Architecture](docs/ARCHITECTURE.md) | Components, authorization and compatibility contracts |
| [Coding standards](docs/CODING_STANDARDS.md) | Conventions and full verification commands |
| [Plan](plan.md) | Current scope, remaining work and original acceptance criteria |
| [Security background](docs/background.md) | Original incident sources, control rationale and historical build prompts |
| [Document adapter](docs/document-adapter.md) / [model adapter](docs/model-adapter.md) | Detailed integration boundaries |
| [Presenter handout](docs/Secure_Agent_Lab_Handout.md) / [Word export](docs/Secure_Agent_Lab_Handout.docx) | Infrastructure presentation materials |
| [Pilot readiness](docs/PILOT_READINESS.md) | Separate production-readiness gate |

## Evidence and limitations

Artifacts retain one latest set per case under `artifacts/<case>/latest`; counters and leases live in `artifacts/.runs`. Rerunning replaces earlier evidence. Save needed diagnostics elsewhere. Browser Clear removes displayed snapshots only. Follow launcher cleanup instructions for Docker resources.

All demonstrations use synthetic data. This is not a production security platform. Desktop processes retain their user's OS permissions; dedicated Docker exercises demonstrate OS/network isolation. New live-provider calls, remote CI and container runs require separate verification. Never commit credentials or generated runtime artifacts.
