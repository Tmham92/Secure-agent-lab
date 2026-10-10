# Refactoring execution record

Baseline revision: 27df2dd2f710ab0876373c7cded7a1d13c27f8b8, plus preserved local launcher/API fixes and plan documents.
Validated locally on 9 October 2026 with SDK 10.0.400 and PowerShell 7.6.5. Source and original built assemblies were backed up outside the repository before edits.

## Phase status

| Phase | Result |
|---|---|
| R0 baseline | Recorded original checks, desktop sequence, ten portable pairs and original assemblies |
| R1 standards | EditorConfig, pinned SDK, SDK analyzers/style enforcement and CI format/layout/environment gates implemented; local gates passed |
| R2 Core/Transport | Types separated into responsibility folders with public signatures and serialized member order retained; checks passed |
| R3 hosts | Endpoint groups, authentication, startup validation, registration/pipeline and small entry points implemented; host checks and launchers passed |
| R4 durable | State persistence/recovery, publication binding, audit/verification and budget/controller responsibilities separated; crash/recovery and original-state compatibility passed |
| R5 collaboration | Contracts, broker/evaluator boundaries, pure scoring, endpoints and actors separated; checks and desktop/container demos passed |
| R6 comparisons/checks | Named scenario classes, container roles, behavior cases and fixtures implemented; original case identities retained and all twelve pairs passed |
| R7 scripts/docs | Environment restoration regression retained; layout/dependency/syntax guard, architecture, standards and demo documentation updated; launchers passed |
| R8 closeout | Local acceptance passed; remote GitHub CI remains pending for these working-tree changes |

## Acceptance evidence

- Release solution build: zero warnings and errors. Formatter verification passed.
- Source filename/dependency/PowerShell syntax guard and absent/empty/nonempty environment regression passed.
- Windows: 17 core, 16 transport, 20 durable, 14 document, 12 model, 18 collaboration and 13 runtime checks: 110 total.
- Linux: same matrix plus final-symlink and FIFO document checks: 112 total. Ran as an unprivileged user in a disposable container with no network, read-only root, dropped capabilities and temporary writable fixture storage.
- All original case names in the six main harnesses match the recorded Windows baseline. Runtime checks retain all 13 assertions, including three crash points.
- Basic → durable → basic → documents → offline model → response drill → collaboration launch sequence passed in one Windows session.
- Isolated lab: both workers passed bypass probes and authorized proposals; two permitted reads, zero publications, protected canary unchanged.
- Isolated collaboration: independent evaluator/container workflow passed.
- All twelve comparison pairs passed. Actual fake-secret transfer was one unsafe/zero secure; late write was one unsafe/zero secure; permitted reads and preserved earlier effects passed.
- Original binaries created signed state/checkpoints, a credential, a committed publication and an unused approval. Final binaries verified these, replayed without duplicating publication, and redeemed the unused approval. Raw keys/state remain private scratch material outside tracked source.

Local logs are under ignored artifacts/refactoring-baseline. Container comparison summary: artifacts/comparisons/suite-684eb33b14f14ab6a01c5ddfc560b2d4/summary.md. Isolated runs: securelab-425b2fc0fe7a and collab-05bee3fe43da. Runtime fixture directories and private keys are not deliverables.

The initial disposable Linux test configuration used a symlink for fixture storage, correctly rejected by the safe reader; a second configuration lacked write ownership. Final verification used directly mounted storage owned by the test user. No security checks were weakened to accommodate the harness.

## Compatibility and remaining limits

At the initial refactor closeout, existing demo commands, CLI selectors, public namespaces, contracts, credentials, approval bindings, persisted state and audit semantics were retained. The namespace follow-up below supersedes the C# namespace decision. Coordinators deliberately keep whole-operation locks rather than splitting authorization/effect commitment into independently locked fragments. No new packages, real resources or worker privileges were introduced.

The single consolidated handout remains the presenter reference; presentation commands and effects are unchanged. The lab remains synthetic, with local durable coordination and volatile broker state; production identity, distributed persistence and live provider accounting remain separate work.

No changes have been committed or pushed for this refactor. Remote CI must be observed after publication before the plan's full acceptance gate can be closed.

Final verification repeated on 10 October 2026: all 17 projects built with zero warnings/errors; 110 Windows checks, formatter, source-layout/syntax and environment restoration gates passed. Logs: artifacts/final-check-20261010-073159. A documentation-only review subsequently corrected old pending notes in the isolation/document/model guides and added docs/DEMO_REHEARSAL.md.

## Folder namespace alignment 10 October 2026

Namespaces now follow project folders in source and executable checks, including Cases and Fixtures. All callers, qualified references and static imports are updated together. Top-level Program.cs files use imports and remain valid top-level executables. Test-SourceLayout now guards folder namespace alignment as well as type filenames, dependency boundaries and script syntax.

This is a source API change: consumers of the old namespaces must update their usings and rebuild. HTTP/JSON property names, credential payloads, signed/persisted formats and demo commands are intended to remain unchanged. This supersedes the original refactor's decision to retain old namespaces. Validation for the namespace migration is recorded below separately from earlier acceptance runs.

Namespace migration acceptance on 10 October 2026:

- All 17 projects build in Release with the normal analyzers/style/import gates enabled: zero warnings/errors.
- All 110 Windows security/runtime checks passed. Desktop basic → durable → basic → document → offline model → collaboration sequence passed.
- Formatter verification, folder namespace/type filename/dependency/PowerShell syntax guard and environment restoration regression passed.
- Both rebuilt Linux container demos passed: isolation project securelab-b17616d9f645 and collaboration project collab-272a32567bae. Isolation retains two permitted reads, zero publications and an unchanged protected canary; collaboration retains two messages and one authorized submission.
- All twelve comparison pairs passed; suite index artifacts/comparisons/suite-d2f1d4e46bf24499af25d07a13d1ddb3/summary.md. Fake-secret capture remains unsafe one/secure zero, and late writes unsafe one/secure zero with earlier effects preserved.
- A fresh old-binary compatibility fixture passed verification using the namespace-aligned assemblies: persisted state/checkpoints, credential/grant binding, idempotent replay and unused approval redemption remain compatible. Private generated keys/state are excluded from deliverables.

Logs are ignored under artifacts/namespace-verification; final build log artifacts/namespace-final-build.log. The standalone full 112-check Linux matrix retains its earlier 9 October result; this migration reran Linux container demos rather than claiming a new full Linux harness run. Remote CI remains pending. No commit/push was performed.

## Bounded artifact retention 10 October 2026

Generated test/demo evidence now uses artifacts/<case>/latest. Each admitted attempt overwrites that case's previous set and records a monotonically increasing RunNumber in run.json; artifacts/.runs retains counters and exclusive case leases. Concurrent attempts for one case are rejected before evidence is removed. Random security identifiers and Docker project names remain independent of storage filenames.

Validation: all 17 Release projects build with zero warnings/errors; all 110 Windows checks and desktop demos passed twice. The retention regression passed replacement, counter, active-run exclusion, traversal rejection, link-safe cleanup/ownership and corrupt-counter checks. All twelve comparison pairs passed twice with the same 114 generated comparison files and working relative summary links. Source layout, PowerShell syntax and environment restoration checks passed. Both isolated container demos passed twice, including authorized proposals/collaboration and bypass checks.

The one-time migration removed 64 recognized legacy generated entries. Earlier historical log paths in this document are therefore no longer retained. Current evidence is under each case's latest directory. FinishedUtc records disposal, not a passing verdict; read the case output/evidence. Save evidence elsewhere before rerunning if historical retention is needed. Remote CI is pending; no commit or push was performed.
