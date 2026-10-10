# Refactored architecture and compatibility

## Project responsibilities

| Project | Owns |
|---|---|
| Core | Grants, policy, synthetic execution, safe document adapters, proposals and local audit chain |
| Transport | Educational credentials and HTTP request/response contracts |
| Durable | Persisted models, file coordination, signed audit, approval binding, budgets and transactional gateway |
| Api | Validated gateway options, dependency registration, authentication, pipeline and endpoint groups |
| AuditCollector | Separate authenticated HTTP host for independently signed audit storage |
| Collaboration | Scoped message broker, transcript contracts, independent evaluation and container actors |
| ModelHost | Trusted one-shot bounded provider client; optional and offline by default in demos |
| Worker | Untrusted proposal walkthrough and bounded isolation probes |
| Operator | Host-only credential command entry point |
| Demo | Offline deterministic walkthrough |
| Comparisons | Fixed paired scenarios, vulnerable fixtures, safe evidence and disposable container roles |
| Six Checks projects | Named cases, assertions/fixtures and executable check registration; not production dependencies |

## Transaction ownership

Gateway owns its in-process gate. Authorization, budget charges, approval consumption, mock execution and audit insertion remain inside that gate.

DurableGateway owns the directory lease for a complete operation. It loads state, reconciles containment, evaluates/changes domain state and commits through DurableStateStore before releasing the lease. DurableStateStore performs pending-state authentication/reconciliation and the existing prepare → collector acknowledgement → state write → pending-file removal sequence. It does not acquire its own fragmentary lease. Fault hooks remain after preparation, audit acknowledgement and state commitment. Containment markers are still checked when the collector is unavailable.

DurableBudgetStore owns the shared budget-file lease. Reserve/check/update remain atomic for local coordinators. BoundedRunController owns timeout/cancellation/quarantine policy; cancellation is not a spend refund or proof of completion.

Broker owns one lock for grant validation, quotas, mailbox delivery/consumption and method evidence. IndependentEvaluator owns its challenge lock and consumes only a signature-valid, bound scoring attempt. AuthorizedMethodEvaluator is pure scoring called after that verification; it owns no keys or mutable challenge state.

## Compatibility contracts

Namespaces now match each project and folder (for example SecureAgentLab.Core.Contracts and SecureAgentLab.Durable.Persistence). Assemblies and public record member order remain stable. C# consumers must update imports and rebuild; persisted/HTTP data does not encode these namespace names. HTTP schemas, HMAC credential format, exact-content binding and independently signed checkpoint/transcript bytes retain their existing representation. No persisted member was renamed for style. Private-field renaming preserves inferred anonymous JSON property names explicitly where necessary.

LabApi.Build and other host factories remain available to check harnesses. Program.cs delegates startup; existing script selectors, ports, readiness output, effect assertions and artifact names remain supported. ResponsesProposalClient.Generate remains a compatibility wrapper around GenerateAsync.

A private compatibility probe uses pre-refactor assemblies to create state, checkpoint evidence, a credential, a committed publication and an unused approval. Refactored binaries must reopen and verify that state, replay the committed action once and redeem the unused approval. Keys and raw fixture state are kept outside tracked source and never included in the report.

## Intentional limits

The code remains a synthetic single-host lab. Durable local coordination is not a distributed store. Broker state is volatile. Refactoring does not grant real resources, connect a live model, enable external writes or approve a production pilot. Large remaining coordinators require transaction-level review rather than arbitrary size-driven fragmentation.
