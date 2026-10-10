# Coding standards

These conventions apply to source/check projects and PowerShell launchers. The lab stays synthetic and offline by default.

## Organization and naming

- Keep one public top-level type per matching file; private nested implementation types may remain with their owner.
- Use the project name followed by relative folders as the namespace, including Cases and Fixtures in check projects. Top-level executable statements remain in Program.cs without a namespace declaration.
- Separate hosting, endpoints, authentication, policy, persistence, execution, scenarios and fixtures by responsibility. Keep Core independent of hosts; normal projects must not reference deliberately vulnerable Comparisons.
- Keep entry points focused on composition. Prefer cohesive classes and useful interfaces over arbitrary fragmentation.
- Use PascalCase for types/members, camelCase for parameters/locals, `_camelCase` for private instance fields and PascalCase for private static fields. Preserve serialized member names/order.

## C sharp style

Use file-scoped namespaces, four-space indentation, braces and one statement per line. Favor descriptive names and readable methods over compressed syntax. Use var when the type is apparent.

Use Async for asynchronous implementations and retain compatibility wrappers when needed. Propagate supported cancellation, dispose resources at their owning boundary, catch specific recoverable failures and return stable reason codes. Do not log secrets.

Review methods around 40–60 lines and classes around 250–350 lines for cohesion; these are review prompts, not hard limits. Gateway and broker coordinators may remain larger to retain whole-operation atomicity. Document that ownership in ARCHITECTURE.md.

## Automated gates

The validated SDK is pinned through global.json. Nullable checking, warnings as errors, SDK analyzers at level 10.0 and code style on build are enabled. EditorConfig enforces formatting, braces, folder namespaces (IDE0130) and unnecessary imports (IDE0005). XML documentation output is generated because IDE0005 requires it during builds; only CS1591 is excluded because documentation for every public lab member is not required. Naming suggestions support review.

Test-SourceLayout.ps1 checks public type filenames, folder namespaces, comparison dependency boundaries and PowerShell syntax. It is a lightweight guard, not a complete C# architecture analyzer.

## Security and compatibility

The transaction owner holds its lock from loading/authorization through approval redemption, budget/effect commitment and audit ordering. Extracted components must not independently lock only fragments of that sequence.

Keep HTTP/JSON schemas, enum encoding, credentials, approval binding, hashes/signatures, persisted formats, selectors and evidence compatible. Namespace moves require C# consumers to update imports and rebuild; assembly identities and serialized contracts remain stable. Contract changes require deliberate versioning and migration work.

Validate required configuration before listening. Preserve absent, empty and nonempty environment values distinctly through ProcessEnvironment.ps1. Require HTTPS except for explicitly enabled loopback-only lab connections. Cancellation is not proof of termination, and stop does not undo prior effects.

Keep vulnerable comparison implementations visibly separate from secure hosts. Preserve fixed synthetic inputs, outer limits, actual-effect assertions and positive controls. Never add an unsafe switch to the secure host.

## Checks and full acceptance

Checks use named behavior cases with explicit dependencies and separate fixtures. Preserve case identities and assertions. Console harnesses remain intentional: dotnet test does not discover them. Use deterministic clocks for expiry and bounded actual process/network probes for deployment boundaries.

Run these commands from the repository root. Expect 110 checks on Windows or 112 on Linux, where document checks add final-symlink and FIFO probes.

```powershell
dotnet build SecureAgentLab.slnx -c Release
dotnet format SecureAgentLab.slnx --verify-no-changes --no-restore
./scripts/Test-SourceLayout.ps1
./scripts/Test-ProcessEnvironment.ps1
dotnet run --project tests/SecureAgentLab.Checks -c Release --no-build
dotnet run --project tests/SecureAgentLab.TransportChecks -c Release --no-build
dotnet run --project tests/SecureAgentLab.DurableChecks -c Release --no-build
dotnet run --project tests/SecureAgentLab.DurableChecks -c Release --no-build -- --phase7
dotnet run --project tests/SecureAgentLab.DocumentChecks -c Release --no-build
dotnet run --project tests/SecureAgentLab.ModelChecks -c Release --no-build
dotnet run --project tests/SecureAgentLab.CollaborationChecks -c Release --no-build
```

Complete acceptance also requires the desktop sequence, both isolated demos and all twelve comparison pairs in DEMO_REHEARSAL.md. Run relevant checks after changes and the full matrix before closeout. Compare case identities as well as totals. Inspect final effects and completion; one PASS is insufficient. Record local validation and remote CI separately.

## Contribution checklist

- Explain the problem and resulting behavior; avoid unrelated changes.
- Review source/dependency layout, configuration, resource ownership and failure cleanup.
- Review transaction locks and serialized compatibility for security changes.
- Run build, formatting and relevant security checks; preserve evidence and investigate failures.
- Exclude credentials, private signing material and raw runtime state from commits and shared evidence.
- Update DEMO_GUIDE.md, README.md and plan.md after each phase; keep one consolidated presenter handout.

Package-based test discovery, production identity, distributed persistence and live provider accounting are separate design decisions.

Use Core.Diagnostics.ArtifactRun and scripts/ArtifactRuns.ps1 for disposable lab output. Hold the case lease for the entire run and dispose it after owned resources stop. Preserve random security identifiers; only artifact directory names become stable. Never reuse old durable fixture state to save disk space. ./scripts/Test-ArtifactRetention.ps1 validates this boundary.
