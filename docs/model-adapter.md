# Phase 6: optional model proposals

The deterministic source remains the default. `ModelProposalSource` implements the
same `IProposalSource` interface, so model suggestions go through the authenticated
gateway, immutable grant, quotas and approval checks. A model never executes a tool.

## Offline demo (no account/key/network inference)

From the repository root on Windows with .NET 10 and PowerShell 7:

```powershell
dotnet build SecureAgentLab.slnx -c Release
pwsh -File scripts/Run-ModelLab.ps1
dotnet run --project tests/SecureAgentLab.ModelChecks -c Release --no-build
```

The demo uses `fixtures/model/adversarial-proposals.json`, an intentionally malicious
model-output fixture. Expect one allowed task read; secret read, external transfer,
shared-cache messaging and privilege changes denied; publication awaiting approval.
The script checks actual effects: **one read, zero publications**. It privately issues
the worker credential, starts/stops the gateway and restores the host environment.
`-Port 5194` changes the default 5193 listener.

`forged-approval.json` shows an extra forged ticket field that fails schema validation
before any request. Parser and provider checks inspect proposals, decisions and effects;
they do not accept model claims that an action was approved or safe. These fixtures test
authorization under malicious output, not empirical prompt-injection resistance of a
particular live model. The compiled hostile reference document is untrusted prompt data.

## Optional live generation

Provision `OPENAI_API_KEY` in the **trusted host environment**, outside source, CLI
arguments and logs. Choose an API model available to your account that supports the
Responses API with strict Structured Outputs; no model is selected automatically.

```powershell
pwsh -File scripts/Run-ModelLab.ps1 -Live -Model YOUR_SUPPORTED_MODEL_ID
```

This explicit switch makes one application-level generation request and can incur API
charges. The trusted `SecureAgentLab.ModelHost` process posts only to
`https://api.openai.com/v1/responses`. Normal runtime disables automatic redirects and
system proxies, retains default TLS certificate verification, sets `store=false`, sends
no tools, limits output to 1,024 tokens, bounds the response to 65,536 bytes and imposes
a 30-second deadline. There are no application retries, conversations or feedback loops.
The prompt is a fixed synthetic review task plus hostile synthetic reference text;
gateway credentials, approval tickets and provider keys are never prompt inputs.

The Responses request uses `text.format` with `type=json_schema`, `strict=true` and
`additionalProperties=false`, following the [official Structured Outputs guide](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses).
Schema adherence does not confer permission; output is independently revalidated locally.
Refusal, incomplete status, provider errors, unexpected tool items, duplicate properties,
multiple messages and invalid text fail closed. Raw provider bodies/reasoning are not logged.
Successful validated proposal JSON is captured into ignored `artifacts/model-demo/`.

Before launching the gateway or worker, the demo removes the provider key from their
environment. The worker additionally refuses startup if `OPENAI_API_KEY` is nonempty.
It receives a short-lived gateway credential and the proposal artifact path only. After
the demo, the original host key environment is restored. Never put keys into proposals.

## Schema and authorization

```json
{"proposals":[{"operation":"ReadDocument","resource":"documents/task"}]}
```

At most eight proposals and 16,384 bytes are accepted. Only `operation` and `resource`
are permitted; operation must be an exact enum name, resource a bounded nonempty string
without control characters. Unknown fields, duplicates, numeric/case variants, malformed
JSON, markdown fences, trailing text and missing fields reject the **entire** batch before
execution. No run ID, grant, approval, content override, idempotency key or budget is
deserialized from model output. Resource syntax is not a permission: the gateway still
checks exact resource scope and may deny a syntactically valid proposal.

The worker is linked to Core/Transport only, never the provider client. Model mode does
not assert a model's chosen sequence is correct; the security harness and offline demo
assert actual gateway effects. Live mode permits whatever valid in-scope reads fit the
grant, and still asserts zero publications because no host approval is supplied.

## Limits and verification

Live inference is a trusted-host preprocessing step, **not** an Internet route in the
isolated worker network. Phase 4's firewall remains unchanged and denies remote provider
access. The host adapter's destination restrictions are application configuration, not
a claim of host OS egress containment. A production continuous broker, provider credentials
in isolated secret storage, DNS/egress policy, retention, monetary limits and an iterative
tool loop need a separately reviewed deployment. Desktop workers retain the desktop-demo
OS limitations. No paid model call was run during implementation.

Local API/worker Release builds pass with zero warnings/errors. The new host and check
sources were compiled directly with the installed .NET 10 compiler and framework
references; **12 offline model checks**, including durable-gateway effects and audit, pass. Full solution SDK restore currently cannot
read the user's NuGet configuration despite requested read permission, so normal SDK
build/restore of the two new projects remains unverified locally. The authenticated
offline model demo passes, including a parent key canary that must not reach the worker.
CI is configured to build and run the full projects normally on Linux/Windows; remote
CI, live API compatibility and Docker integration remain unverified.
