# Phase 5: exact document reads

The gateway now optionally reads actual **synthetic local files**, rather than returning
the hardcoded task string. Both in-memory and durable gateways use `IDocumentReader`.
No shell, arbitrary filesystem path, SQL, remote URL or target-service key is a worker tool.

## Run on Windows

From the repository root with .NET 10 and PowerShell 7:

```powershell
dotnet build SecureAgentLab.slnx -c Release
pwsh -File scripts/Run-DocumentLab.ps1
dotnet run --project tests/SecureAgentLab.DocumentChecks -c Release --no-build
```

The demo generates fresh fixtures under ignored `artifacts/document-demo/`, launches
the authenticated gateway with a host-only document root, and runs the separate worker.
It shows one file-backed read, denied external/messaging/permission requests, publication
requiring approval, and one read / zero publication effects. `-Port 5193` changes the
default 5192 listener. Server cleanup and environment restoration are automatic.

The executable checks also issue separate task-only and reference-only grants, read a
hostile reference document as literal data, and verify those grants cannot read each
other's documents. The fixture credential is a synthetic canary, never a real secret.

## Host configuration and authority

`Lab__DocumentRoot` selects an absolute, trusted local directory. Omit it to retain the
earlier hardcoded synthetic reader. The shipped API uses this fixed, compiled catalog:

| Exact ID | Relative file | Authority |
|---|---|---|
| `documents/task` | `task/task.txt` | Existing default task grant |
| `documents/reference` | `reference/reference.txt` | Must be explicitly granted by the host |

The catalog pins SHA-256 of the expected UTF-8 bytes. `New-DocumentFixtures.ps1` produces
those exact bytes without BOM or newline. Changed bytes return `document_version_mismatch`;
the worker cannot choose a path, supply alternate contents, refresh the hash or change
the catalog. Default task grants do not include the reference document. A host can use
the library's validated catalog constructor for a reviewed local fixture; the API does
not expose catalog administration. Authorizing real data or more IDs is future work.

Target-service credentials are unnecessary for this filesystem adapter. Host signing
keys, document-root configuration and privileged storage stay in the gateway identity.
Desktop demos still share the Windows account, so they demonstrate gateway enforcement,
not resistance to direct OS access. The container deployment now mounts the generated
fixture tree read-only **only into the gateway**, as `/protected/documents`. The worker
does not receive that mount or document-root configuration and must use the proposal relay.
Container runtime acceptance passed locally on 9 October 2026; remote CI for the final revision remains pending.

## File boundary and failures

All requested IDs must match the authenticated immutable task grant and exact catalog
entry. Comparison is ordinal. Relative paths come from the host catalog; validation
rejects absolute paths, traversal, empty segments, backslashes, ADS, escapes and aliases.
Every directory and final file is opened without following symlinks/reparse points.

Linux opens components relative to directory descriptors using `openat`, `O_NOFOLLOW`
and `O_DIRECTORY`, then uses `statx` on the opened descriptor to require a regular file.
Nonblocking open prevents FIFO opens hanging before the type check. Windows opens and
verifies the canonical root, then uses `NtCreateFile` relative to pinned parent handles,
`FILE_OPEN_REPARSE_POINT`, and file-type/attribute checks. Relative opens avoid ancestor
replacement races; Windows sharing denies modification/deletion while handles are held.
The implementation follows [Microsoft's native-open contract](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile)
and [Linux descriptor metadata semantics](https://man7.org/linux/man-pages/man2/statx.2.html).

The adapter reads at most the lesser of the remaining response budget and the 4,096-byte
document ceiling, plus one sentinel byte. It never allocates or returns an unbounded file.
Oversized data is denied with no partial response. Strict UTF-8 rejects malformed input;
hash validation rejects changed content. A successful read deducts measured UTF-8 bytes,
including multibyte characters. Active denied attempts still consume the existing call
budget, but not response bytes or successful-read effects. Missing/inaccessible files,
links and special files return `document_unavailable` without path or exception details.

Document text is data; the gateway never parses it as policy, a grant or approval.
The durable gateway records content hash and response size without logging document
bodies or host paths, and preserves budgets across restart. Existing synthetic publication
semantics remain unchanged. OS administrators, trusted mount configuration and hash/catalog
ownership remain trusted; this is not a general hostile-filesystem or remote-share adapter.

## Verification status

Release build: zero warnings/errors. Windows: **14/14 document checks** and the separate
file-backed demo passed. Existing 17 offline, 16 transport and 20 durable checks are
regression requirements. CI now runs the document harness on both Linux and Windows.
Linux adds final-file symlink and FIFO probes (**16 checks total**); all sixteen passed locally in the refactoring acceptance run on 9 October 2026. Windows document checks passed again on 10 October. The full isolation demo also passed locally; its deployment boundaries and remaining remote CI gate are documented separately in deploy/isolation/README.md.
