# Playground verification — 12 October 2026

Verified locally on Windows using .NET SDK 10.0.400:

| Check | Result |
|---|---|
| Release build of SecureAgentLab.slnx | PASS, zero warnings/errors |
| PlaygroundChecks | 17/17 PASS, no provider calls |
| Existing Core / Transport / Durable / Document / Model / Collaboration / Phase 7 checks | 110/110 PASS |
| Existing portable comparison pairs | 10/10 PASS |
| Source-layout, process-environment and artifact-retention scripts | PASS |
| Solution dotnet format verification | PASS |
| git diff --check | PASS |
| Browser: injection comparison | PASS; fake-secret outbox capture in baseline only; summaries in both |
| Browser: exact-message approval | PASS; reviewed message appears once in hardened simulated outbox |
| Browser: runaway execution | PASS; hardened stops at configured call budget |
| Browser: reset, benign task and escaped output | PASS; script-shaped input renders as text |
| Browser: 390-pixel mobile viewport | PASS; no horizontal overflow |

Browser verification used local headless Chrome via the bundled Playwright library after the built-in browser tool failed to start due to the environment's sandbox helper error. Desktop and mobile screenshots were visually inspected. Browser testing caught and fixed Release-mode framework asset routing and control label/selection issues. No browser page errors occurred in the completed workflow.

Evidence in ignored artifacts/: playground-desktop.png, playground-mobile.png, playground-browser-results.txt, and local host logs. Existing comparison evidence remains under artifacts/comparisons/portable/latest. Console checks emit their assertions directly; this document records the observed result rather than claiming durable audit retention.

Not performed: real OpenAI model requests, new remote CI execution, new container/isolation runs. Optional live mode has a mocked HTTP integration check and malformed-output checks; provider-specific runtime behavior remains unverified. Existing transport tests emit their own pre-existing temporary Data Protection warnings; the solution build is warning-free.

Existing uncommitted documentation and script work was preserved. The new learning entry point adds a separate Blazor project and check project, reuses the Core file reader, and does not replace or delete advanced labs.

## Advanced extension — verified 13 October 2026

- Complete Release solution build: PASS, zero warnings/errors.
- PlaygroundChecks: 23/23 PASS, including all ten actual advanced comparisons, both actual suites, unknown-ID/missing-build handling, concurrent admission and child-process cancellation/recovery.
- Existing portable CLI: 10/10 comparison pairs PASS after wiring Program.cs to the existing hosting implementation. Original CLI modes remain supported.
- Source-layout/dependency-boundary check: PASS. The web host has no reference to the deliberately vulnerable Comparisons assembly.
- Browser: all ten advanced comparisons produced twenty observation panels with measured effects and positive controls; PASS.
- Browser: durable and collaboration suites passed, displaying their actual output; PASS.
- Browser: separate tabs start with separate result lists; PASS.
- Browser: Docker card command/prerequisites displayed and no web-run button exposed; PASS. No new Docker run performed.
- Browser: cancellation, subsequent rerun, clearing results and 390-pixel mobile layout; PASS. Mobile result screenshot visually inspected; no page errors.
- Basic browser workflows rerun after adding navigation: PASS (injection comparison, exact approval, budget stop, reset, escaped text and mobile viewport).
- Solution formatting and git whitespace verification: PASS after correcting changed comparison source line endings.

Ignored local evidence: artifacts/advanced-browser-results.txt, artifacts/advanced-desktop.png, artifacts/advanced-mobile.png, artifacts/playground/advanced/<scenario>/latest/comparisons.json and summary.md, plus original suite artifact cases. The recorded pass describes local synthetic execution, not production security assurance. New remote CI and Docker/isolation runtime validation remain pending; no paid provider calls were made.

## Cleanup regression — 13 October 2026

After documentation consolidation and removal of duplicate startup/harness bodies:

| Check | Result |
|---|---|
| Release solution build | PASS; zero warnings/errors |
| Original Windows checks | 110/110 PASS, including all 13 runtime checks |
| Playground checks | 23/23 PASS; basic and actual advanced runner integrations retained |
| Portable comparisons | 10/10 PASS |
| Source layout, environment restoration, artifact retention | PASS |
| Solution formatting / whitespace | PASS |
| Updated local Markdown links | PASS |
| Desktop gateway launch after startup consolidation | PASS; one scoped read, zero publications, expected denials/approval requirement |

All named case files were retained and activated through their harness registrations. The cleanup changes no UI controls, provider protocol, serialized contract, transaction lock or Docker deployment. Previous browser observations above remain dated observations, not a claim of a new full browser/Docker rehearsal. Details and remaining limits are in docs/REFACTORING_RESULTS.md.
