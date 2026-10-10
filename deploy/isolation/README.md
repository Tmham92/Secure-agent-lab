# Phase 4: isolated synthetic worker

Run from the repository root with PowerShell 7 and Docker Desktop in **Linux containers** mode:

```powershell
pwsh -File scripts/Run-IsolatedLab.ps1
```

The script builds dependencies in SDK images, generates an ephemeral host signing key,
starts the trusted network guardian, gateway and proposal relay, privately issues a
short-lived run credential, and launches two restricted worker containers in sequence.
Each first attempts direct bypasses, then runs the normal five-proposal scenario.
The script checks deployed privileges, mounts and numerical resource limits, prints
mock effect counts, and removes its containers and networks on exit. No SDK or package
restore runs inside the worker. Ignored `artifacts/isolation` contains synthetic fixture
files only. Images remain available for a later run.

## Boundary

The trusted guardian installs IPv4 and IPv6 OUTPUT policies before dependent services
start. Both default to DROP. UID 1654 (worker) can send TCP only to `127.0.0.1:8080`.
UID 1655 (relay) can contact only the fixed gateway at `127.0.0.1:5188` and reply to
local clients. UID 1656 (gateway/operator) can use loopback; every remote destination,
including Docker/host bridges, metadata, private networks, DNS and public internet,
is denied. The Docker network is internal and has no published ports.

The worker, relay and gateway share a **network namespace**, but have separate PID,
mount and user identities. Firewall rules match the sending socket's UID. The worker
cannot change UID, firewall rules or mounts: it has no capabilities, runs as 1654,
has `no-new-privileges`, a read-only root, and no host/socket/secret mounts. Each worker
gets fresh private `/workspace` and `/tmp` tmpfs. Limits are 128 MiB RAM, half a CPU,
32 processes, and 8 MiB per tmpfs. Two sequential workers verify scratch is not reused.

Phase 5 generates pinned task/reference files in the protected fixture tree and configures
`Lab__DocumentRoot=/protected/documents` in the gateway only. Reads now use the bounded
file adapter; the worker has no protected mount or file-root configuration. See
[document adapter](../../docs/document-adapter.md) for path, link and content checks.

The relay accepts only POST to the exact proposal route, rejects query strings and all
other paths/methods, limits request size and rate, and uses a fixed upstream and Host.
It never interprets worker-supplied destinations. It cannot call remote URLs or DNS.
Host authorization is still checked by the gateway; the relay does not approve tools.
There is no model provider or arbitrary HTTP executor in this phase. Redirects, rebinding
and indirect exfiltration have no authorized remote first hop. DNS receives no permission.
Adding a provider requires a separate narrow broker and new network/redirect/DNS tests.

## Evidence

The relay keeps all Nginx temporary paths (body, proxy, FastCGI, uWSGI and SCGI)
and its PID file in its existing bounded `/tmp` tmpfs. Nginx initializes even unused
module temp paths on startup; leaving their defaults under `/var/cache/nginx` caused
the read-only-root startup failure captured in `securelab-b987cf7c3d24` diagnostics.

Before the bypass scenarios, a separate restricted worker runs `--relay-readiness`:
an unauthenticated POST through the relay must return backend 401 within ten bounded
attempts. This proves the permitted worker/relay/backend path without adding tool effects.
On failure, worker console output is retained in `artifacts/isolation/latest/worker-*.log`.
Before deleting containers, the launcher saves service status, relay logs/identity/config
validation and IPv4/IPv6 firewall counters as `diagnostics-*.txt`. It never dumps full
container environment/inspection or issuer credentials. Failed CI jobs upload only these
diagnostic files as `isolation-diagnostics` for seven days. Actions run #2 passed Linux
and Windows checks, but timed out on relay HTTP. Subsequent local runs with the relay fixes passed; remote isolation CI for the final revision remains pending.

The operator confirms the backend is alive on **both IPv4 and IPv6** and that a protected
other-run storage canary exists. The worker then bypasses its normal client with direct
sockets: both backend listeners, metadata, private ranges and documentation-only external IPv4/IPv6 addresses must
be unreachable. It also sends a raw DNS query, tries administrative/traversal/query
routes through the relay, checks effective capabilities and root mount flags, verifies
protected storage is absent, and confirms writable private scratch starts empty.
Finally an unauthenticated proposal must reach the backend and return 401, and the
authenticated deterministic worker must produce Allowed / Denied / Denied / Denied /
ApprovalRequired. Thus an unavailable gateway cannot make the suite falsely succeed.
The script independently checks actual Docker configuration after each successful run.
Failures exit nonzero. GitHub Actions runs this demo on a Linux Docker host.

## Scope and acceptance status

This focused isolation demo uses the **in-memory synthetic gateway**. Run
`scripts/Run-DurableLab.ps1` separately for phase 3 restart/approval/signed audit evidence.
The container demo does not yet combine persistent signed audit with network isolation,
and does not publish a report or call real tools. Cleartext HTTP is confined to the
namespace's loopback using the existing explicit lab exception. Production deployment
still requires authenticated TLS, external durable audit and key lifecycle management.

The host, Docker Engine, kernel, guardian, relay, gateway and operator are trusted.
The guardian alone receives NET_ADMIN in its container namespace. Container/kernel
escape, a compromised trusted UID, or Docker host administration are outside this
boundary. Fixed distinct UIDs are essential: do not run an untrusted process under
gateway/relay UID or add another network attachment. Loopback and network metadata
are shared, so this does not claim kernel-level separation of trusted services.

Local container acceptance passed again after refactoring on 9 October 2026: both workers passed bypass/deployment checks and authorized proposals; two reads, zero publications and the protected canary unchanged. The full 17-project Release build and 110 Windows checks passed again on 10 October. Remote CI remains a separate pending gate. See ../../docs/DEMO_REHEARSAL.md for a complete demo run.
