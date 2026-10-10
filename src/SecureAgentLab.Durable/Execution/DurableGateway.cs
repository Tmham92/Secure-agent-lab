using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Core.Policy;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Persistence;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Durable.Execution;
// All publication effects are rows in the durable synthetic ledger, not external writes.
// That deliberate boundary makes approval consumption and mock execution one transaction.
public sealed class DurableGateway : ILabGateway
{
    private readonly string directory;
    private readonly string statePath;
    private readonly string pendingPath;
    private readonly IAuditCollector collector;
    private readonly CheckpointVerifier verifier;
    private readonly byte[] integrityKey;
    private readonly string policyVersion;
    private readonly TimeProvider clock;
    private readonly Action<DurabilityPoint>? crash;
    private readonly DefaultPolicy policy = new();
    private readonly IDocumentReader documents;
    private readonly bool reportWrites;
    public DurableGateway(string directory, IAuditCollector collector, string publicKey, string streamId, byte[] integrityKey, string policyVersion = "synthetic-v1", TimeProvider? clock = null, Action<DurabilityPoint>? crash = null, IDocumentReader? documents = null, bool enableReportWrites = false)
    {
        if (integrityKey.Length < 32 || string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new ArgumentException("Invalid durable configuration.");
        }

        this.directory = Path.GetFullPath(directory);
        this.collector = collector;
        verifier = new(publicKey, streamId);
        this.integrityKey = integrityKey.ToArray();
        this.policyVersion = policyVersion;
        this.clock = clock ?? TimeProvider.System;
        this.crash = crash;
        this.documents = documents ?? new SyntheticDocumentReader();
        reportWrites = enableReportWrites;
        statePath = Path.Combine(this.directory, "state.json");
        pendingPath = Path.Combine(this.directory, "pending.json");
        Guard(() =>
        {
            using IDisposable lease = DurableFiles.Lock(this.directory);
            Load();
            return true;
        });
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or CryptographicException or HttpRequestException or TaskCanceledException)
        {
            throw new AuthorizationDependencyException("authorization_dependency_unavailable", e);
        }
    }

    private string Seal(PreparedChange change) => Convert.ToHexString(HMACSHA256.HashData(integrityKey, JsonSerializer.SerializeToUtf8Bytes(change)));
    private bool Same(SignedCheckpoint a, SignedCheckpoint b) => a.Value == b.Value;
    private void Validate(DurableSnapshot snapshot)
    {
        if (snapshot.Domain is null || snapshot.Checkpoint is null || snapshot.Domain.Sessions is null || snapshot.Domain.Approvals is null || snapshot.Domain.Publications is null)
        {
            throw new AuthorizationDependencyException("invalid_state");
        }

        verifier.Verify(snapshot.Checkpoint);
        string? expected = snapshot.Checkpoint.Value.Sequence == 0 ? new DomainState().Digest() : snapshot.Checkpoint.Value.StateDigest;
        if (snapshot.Domain.Digest() != expected)
        {
            throw new AuthorizationDependencyException("state_tampered");
        }
    }

    private DurableSnapshot Load()
    {
        SignedCheckpoint head = collector.GetCheckpoint();
        verifier.Verify(head);
        DurableSnapshot snapshot;
        if (!File.Exists(statePath))
        {
            if (head.Value.Sequence != 0 || File.Exists(pendingPath))
            {
                throw new AuthorizationDependencyException("state_missing");
            }

            snapshot = new(new DomainState(), head);
            DurableFiles.Write(statePath, snapshot);
        }
        else
        {
            snapshot = DurableFiles.Read<DurableSnapshot>(statePath);
        }

        Validate(snapshot);
        if (File.Exists(pendingPath))
        {
            SealedChange pending = DurableFiles.Read<SealedChange>(pendingPath);
            byte[] expected;
            byte[] actual;
            try
            {
                expected = Convert.FromHexString(Seal(pending.Change));
                actual = Convert.FromHexString(pending.AuthenticationCode);
            }
            catch (FormatException e)
            {
                throw new AuthorizationDependencyException("pending_tampered", e);
            }

            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw new AuthorizationDependencyException("pending_tampered");
            }

            PreparedChange change = pending.Change;
            verifier.Verify(change.Previous);
            if (change.Event.StateDigest != change.Domain.Digest() || change.Event.Hash != AuditChain.ComputeHash(change.Event) || change.Event.Sequence != change.Previous.Value.Sequence + 1 || change.Event.PreviousHash != change.Previous.Value.Hash)
            {
                throw new AuthorizationDependencyException("pending_invalid");
            }

            if (snapshot.Checkpoint.Value.Sequence == change.Event.Sequence && snapshot.Checkpoint.Value.Hash == change.Event.Hash)
            {
                if (!Same(snapshot.Checkpoint, head))
                {
                    throw new AuthorizationDependencyException("audit_head_mismatch");
                }

                File.Delete(pendingPath);
                return ReconcileContainment(snapshot);
            }

            if (!Same(snapshot.Checkpoint, change.Previous))
            {
                throw new AuthorizationDependencyException("pending_conflict");
            }

            if (!Same(head, change.Previous) && (head.Value.Sequence != change.Event.Sequence || head.Value.Hash != change.Event.Hash))
            {
                throw new AuthorizationDependencyException("audit_head_mismatch");
            }

            SignedCheckpoint acknowledged = collector.Append(change.Event);
            verifier.Verify(acknowledged);
            if (acknowledged.Value.Sequence != change.Event.Sequence || acknowledged.Value.Hash != change.Event.Hash || acknowledged.Value.StateDigest != change.Domain.Digest())
            {
                throw new AuthorizationDependencyException("audit_ack_invalid");
            }

            snapshot = new(change.Domain, acknowledged);
            DurableFiles.Write(statePath, snapshot);
            File.Delete(pendingPath);
        }
        else if (!Same(snapshot.Checkpoint, head))
        {
            throw new AuthorizationDependencyException("audit_head_mismatch");
        }

        return ReconcileContainment(snapshot);
    }

    private DurableSnapshot ReconcileContainment(DurableSnapshot snapshot)
    {
        foreach (string? run in snapshot.Domain.Sessions.Keys.ToArray())
        {
            StoredSession session = snapshot.Domain.Sessions[run];
            if (!session.Revoked && HasRevokeMarker(run))
            {
                snapshot.Domain.Sessions[run] = session with
                {
                    Revoked = true
                };
                snapshot = Commit(snapshot, snapshot.Domain, "run_revoked", run, null, new(Outcome.Allowed, "run_revoked"));
            }
        }

        if (!snapshot.Domain.Stopped && HasStopMarker())
        {
            snapshot.Domain.Stopped = true;
            snapshot.Domain.Approvals.Clear();
            snapshot = Commit(snapshot, snapshot.Domain, "gateway_stopped", null, null, new(Outcome.Allowed, "gateway_stopped"));
        }

        return snapshot;
    }

    private DurableSnapshot Commit(DurableSnapshot current, DomainState domain, string type, string? run, Proposal? proposal, Decision decision, string? approval = null)
    {
        DateTimeOffset now = clock.GetUtcNow();
        if (now < current.Checkpoint.Value.UtcTime)
        {
            throw new AuthorizationDependencyException("clock_regressed");
        }

        // Only identifiers and hashes reach the collector. Arbitrary strings and content do not.
        Proposal? safeProposal = proposal is null ? null : new(proposal.Operation, DefaultPolicy.IsSupported(proposal.Operation, proposal.Resource) ? proposal.Resource : "sha256:" + DurableFiles.Hash(proposal.Resource ?? ""), proposal.EstimatedBytes, ExpectedVersion: proposal.ExpectedVersion);
        var entry = new AuditEntry(current.Checkpoint.Value.Sequence + 1, now, run is null ? null : DurableFiles.Hash(run), safeProposal, decision.Outcome, decision.Reason, current.Checkpoint.Value.Hash, "", type, policyVersion, proposal?.Operation == Operation.PublishReport ? ContentHash(proposal) : proposal?.Operation == Operation.ReadDocument && decision.Result is not null ? SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(decision.Result)) : null, approval, Guid.NewGuid().ToString("N"), decision.Result is null ? 0 : Encoding.UTF8.GetByteCount(decision.Result), domain.Digest());
        entry = entry with
        {
            Hash = AuditChain.ComputeHash(entry)
        };
        var change = new PreparedChange(domain, current.Checkpoint, entry);
        DurableFiles.Write(pendingPath, new SealedChange(change, Seal(change)));
        crash?.Invoke(DurabilityPoint.AfterPrepared);
        SignedCheckpoint acknowledged = collector.Append(entry);
        verifier.Verify(acknowledged);
        if (acknowledged.Value.Sequence != entry.Sequence || acknowledged.Value.Hash != entry.Hash || acknowledged.Value.StateDigest != domain.Digest())
        {
            throw new AuthorizationDependencyException("audit_ack_invalid");
        }

        crash?.Invoke(DurabilityPoint.AfterAuditAcknowledged);
        var next = new DurableSnapshot(domain, acknowledged);
        DurableFiles.Write(statePath, next);
        crash?.Invoke(DurabilityPoint.AfterStateCommitted);
        File.Delete(pendingPath);
        return next;
    }

    public string CreateSession(TaskGrant grant) => Guard(() =>
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        DurableSnapshot current = Load();
        if (HasStopMarker() || current.Domain.Stopped || grant.ExpiresAt <= clock.GetUtcNow() || grant.PolicyVersion != policyVersion)
        {
            throw new InvalidOperationException("Inactive or incompatible grant.");
        }

        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        current.Domain.Sessions.Add(id, new(StoredGrant.From(grant), grant.CallLimit, grant.ResponseByteLimit));
        Commit(current, current.Domain, "grant_issued", id, null, new(Outcome.Allowed, "grant_issued"));
        return id;
    });
    public TaskGrant? GetGrant(string runId) => Read(d => d.Sessions.TryGetValue(runId, out StoredSession? s) ? s.Grant.ToGrant() : null);
    public SessionSnapshot? GetSession(string runId) => Read(d => d.Sessions.TryGetValue(runId, out StoredSession? s) ? new SessionSnapshot(s.Grant.ExpiresAt, s.Calls, s.Bytes, s.Revoked || HasRevokeMarker(runId)) : null);
    public MockEffects GetEffects() => Read(d => new MockEffects(d.Reads, d.Publications.Count));
    private static long ReportVersion(DomainState d) => d.Publications.Values.Select(p => p.ReportVersion).DefaultIfEmpty().Max();
    public ReportSnapshot GetReport()
    {
        if (!reportWrites)
        {
            throw new InvalidOperationException("Versioned reports are disabled.");
        }

        return Read(d => d.Publications.Values.Where(p => p.ReportVersion > 0).OrderByDescending(p => p.ReportVersion).Select(p => new ReportSnapshot(p.ReportVersion, p.ReportContent)).FirstOrDefault() ?? new ReportSnapshot(0, null));
    }

    public void RecordRuntimeDenial(string run, string reason) => Guard(() =>
    {
        if (reason is not ("runtime_deadline" or "runtime_revoked" or "runtime_stopped" or "runtime_call_budget" or "runtime_token_budget" or "runtime_cost_budget" or "runtime_fanout" or "runtime_retry_budget" or "runtime_cancelled" or "tool_timeout"))
        {
            throw new ArgumentException("Unknown runtime reason.");
        }

        using IDisposable lease = DurableFiles.Lock(directory);
        DurableSnapshot current = Load();
        Commit(current, current.Domain, "runtime_denied", run, null, new(Outcome.Denied, reason));
        return true;
    });
    private T Read<T>(Func<DomainState, T> read) => Guard(() =>
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        return read(Load().Domain);
    });
    public IReadOnlyList<AuditEntry> GetAuditSnapshot() => Guard(() =>
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        DurableSnapshot current = Load();
        IReadOnlyList<SignedAuditEvent> events = collector.GetEvents();
        verifier.VerifyEvents(events);
        if (events.Count != current.Checkpoint.Value.Sequence || (events.Count > 0 && events[^1].Entry.Hash != current.Checkpoint.Value.Hash))
        {
            throw new AuthorizationDependencyException("audit_snapshot_mismatch");
        }

        return (IReadOnlyList<AuditEntry>)Array.AsReadOnly(events.Select(e => e.Entry).ToArray());
    });
    public static string ContentHash(Proposal proposal) => DurableFiles.Hash(proposal.Content ?? Gateway.DraftReport);
    private static string Binding(string run, Proposal p, StoredGrant grant)
    {
        string original = DurableFiles.Hash(new
        {
            Run = run,
            p.Operation,
            p.Resource,
            p.EstimatedBytes,
            ContentHash = ContentHash(p),
            grant.PolicyVersion,
            Grant = grant.ToGrant().Fingerprint
        });
        return p.ExpectedVersion is null ? original : DurableFiles.Hash(new
        {
            Original = original,
            p.ExpectedVersion
        });
    }

    public PublicationPreview PreviewPublication(string run, Proposal proposal) => Read(d =>
    {
        if (!d.Sessions.TryGetValue(run, out StoredSession? s) || s.Revoked || HasRevokeMarker(run) || HasStopMarker() || d.Stopped || s.Grant.ExpiresAt <= clock.GetUtcNow() || s.Grant.PolicyVersion != policyVersion || proposal.Operation != Operation.PublishReport || ValidateProposal(s, proposal).Outcome != Outcome.Allowed || (reportWrites && proposal.ExpectedVersion != ReportVersion(d)))
        {
            throw new InvalidOperationException("Cannot review this publication.");
        }

        return new PublicationPreview(proposal.Resource, proposal.Content ?? Gateway.DraftReport, ContentHash(proposal), policyVersion, proposal.ExpectedVersion);
    });
    public string ApprovePublication(string runId, Proposal proposal, TimeSpan lifetime) => Guard(() =>
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        using IDisposable lease = DurableFiles.Lock(directory);
        DurableSnapshot current = Load();
        if (!current.Domain.Sessions.TryGetValue(runId, out StoredSession? s) || s.Revoked || HasRevokeMarker(runId) || HasStopMarker() || current.Domain.Stopped || s.Grant.ExpiresAt <= clock.GetUtcNow() || s.Grant.PolicyVersion != policyVersion || proposal.Operation != Operation.PublishReport || ValidateProposal(s, proposal).Outcome != Outcome.Allowed || (reportWrites && proposal.ExpectedVersion != ReportVersion(current.Domain)))
        {
            throw new InvalidOperationException("Cannot approve this publication.");
        }

        string ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string reference = DurableFiles.Hash(ticket);
        DateTimeOffset expiry = clock.GetUtcNow().Add(lifetime);
        if (expiry > s.Grant.ExpiresAt)
        {
            expiry = s.Grant.ExpiresAt;
        }

        current.Domain.Approvals.Add(reference, new(runId, Binding(runId, proposal, s.Grant), ContentHash(proposal), policyVersion, expiry));
        Commit(current, current.Domain, "approval_issued", runId, proposal, new(Outcome.Allowed, "approval_issued"), reference);
        return ticket;
    });
    private Decision ValidateProposal(StoredSession session, Proposal? p)
    {
        Decision decision = policy.Evaluate(session.Grant.ToGrant(), p);
        if (decision.Outcome != Outcome.Allowed)
        {
            return decision;
        }

        if (!reportWrites && p!.ExpectedVersion is not null)
        {
            return Deny("versioned_write_disabled");
        }

        if (reportWrites && p!.Operation == Operation.PublishReport && p.ExpectedVersion is null)
        {
            return Deny("expected_version_required");
        }

        if (p!.Operation == Operation.ReadDocument && p.ExpectedVersion is not null)
        {
            return Deny("malformed_proposal");
        }

        if (p!.Operation == Operation.ReadDocument && p.Content is not null || p.Content is not null && Encoding.UTF8.GetByteCount(p.Content) > 8192)
        {
            return Deny("malformed_proposal");
        }

        return decision;
    }

    public Decision Execute(string? runId, Proposal? proposal, string? approvalTicket = null) => Execute(runId, proposal, approvalTicket, null);
    public Decision Execute(string? runId, Proposal? proposal, string? approvalTicket, string? idempotencyKey)
    {
        try
        {
            return Guard(() =>
            {
                using IDisposable lease = DurableFiles.Lock(directory);
                // Containment markers stay effective even while the collector is unavailable.
                DurableSnapshot current = Load();
                Decision decision = Evaluate(current.Domain, runId, proposal, approvalTicket, idempotencyKey);
                Commit(current, current.Domain, "execution", runId, proposal, decision, approvalTicket is null ? null : DurableFiles.Hash(approvalTicket));
                return decision;
            });
        }
        catch (AuthorizationDependencyException)
        {
            if (HasStopMarker())
            {
                return Deny("gateway_stopped");
            }

            if (runId is not null && HasRevokeMarker(runId))
            {
                return Deny("identity_revoked");
            }

            return File.Exists(pendingPath) ? new(Outcome.RecoveryRequired, "recovery_required") : Deny("authorization_dependency_unavailable");
        }
    }

    private Decision Evaluate(DomainState domain, string? run, Proposal? p, string? ticket, string? idempotencyKey)
    {
        DateTimeOffset now = clock.GetUtcNow();
        if (domain.Stopped || HasStopMarker())
        {
            return Deny("gateway_stopped");
        }

        if (run is null || !domain.Sessions.TryGetValue(run, out StoredSession? session))
        {
            return Deny("unknown_identity");
        }

        if (session.Revoked || HasRevokeMarker(run))
        {
            return Deny("identity_revoked");
        }

        if (now >= session.Grant.ExpiresAt)
        {
            return Deny("identity_expired");
        }

        if (session.Calls == 0)
        {
            return Deny("call_budget_exhausted");
        }

        domain.Sessions[run] = session = session with
        {
            Calls = session.Calls - 1
        };
        if (session.Grant.PolicyVersion != policyVersion)
        {
            return Deny("policy_version_changed");
        }

        Decision policyDecision = ValidateProposal(session, p);
        if (policyDecision.Outcome != Outcome.Allowed)
        {
            return policyDecision;
        }

        if (p!.Operation == Operation.ReadDocument)
        {
            Decision read = documents.Read(p.Resource, session.Bytes);
            if (read.Outcome != Outcome.Allowed)
            {
                return read;
            }

            if (HasStopMarker())
            {
                return Deny("gateway_stopped");
            }

            if (HasRevokeMarker(run))
            {
                return Deny("identity_revoked");
            }

            int bytes = Encoding.UTF8.GetByteCount(read.Result!);
            domain.Reads++;
            domain.Sessions[run] = session with
            {
                Bytes = session.Bytes - bytes
            };
            return read;
        }

        if (ticket is null)
        {
            return new(Outcome.ApprovalRequired, "publication_approval_required");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128 || idempotencyKey.Any(char.IsWhiteSpace))
        {
            return Deny("idempotency_key_required");
        }

        string reference = DurableFiles.Hash(ticket);
        string binding = Binding(run, p, session.Grant);
        string effectKey = DurableFiles.Hash(new
        {
            Run = run,
            Key = idempotencyKey
        });
        if (!domain.Approvals.TryGetValue(reference, out StoredApproval? approval))
        {
            return Deny("approval_unknown");
        }

        if (approval.RunId != run || approval.Binding != binding || approval.PolicyVersion != policyVersion)
        {
            return Deny("approval_mismatch");
        }

        if (now >= approval.ExpiresAt)
        {
            return Deny("approval_expired");
        }

        if (domain.Publications.TryGetValue(effectKey, out PublishedEffect? existing))
        {
            if (existing.Binding != binding || existing.ApprovalReference != reference)
            {
                return Deny("idempotency_conflict");
            }

            return PublicationResponse(domain, run, session, "publication_replayed");
        }

        if (approval.Consumed)
        {
            return Deny("approval_consumed");
        }

        long version = ReportVersion(domain);
        if (reportWrites && (p.ExpectedVersion != version || version == long.MaxValue))
        {
            return Deny("resource_version_conflict");
        }

        Decision response = PublicationResponse(domain, run, session, "publication_approved");
        if (response.Outcome != Outcome.Allowed)
        {
            return response;
        }

        domain.Approvals[reference] = approval with
        {
            Consumed = true
        };
        domain.Publications.Add(effectKey, new(binding, reference, ContentHash(p), p.Resource, reportWrites ? p.Content ?? Gateway.DraftReport : null, reportWrites ? version + 1 : 0));
        return response;
    }

    private static Decision PublicationResponse(DomainState domain, string run, StoredSession session, string reason)
    {
        int bytes = Encoding.UTF8.GetByteCount(Gateway.PublicationResult);
        if (bytes > session.Bytes)
        {
            return Deny("response_budget_exhausted");
        }

        domain.Sessions[run] = session with
        {
            Bytes = session.Bytes - bytes
        };
        return new(Outcome.Allowed, reason, Gateway.PublicationResult);
    }

    private bool HasStopMarker() => File.Exists(Path.Combine(directory, "stopped.json"));
    private bool HasRevokeMarker(string run) => File.Exists(Path.Combine(directory, "revoked-" + DurableFiles.Hash(run) + ".json"));
    // Marker-only interruption never waits for a tool holding the transaction lock.
    // The next responsive Load records these controls in the signed external audit.
    public void SignalRevoke(string run) => DurableFiles.Write(Path.Combine(directory, "revoked-" + DurableFiles.Hash(run) + ".json"), new { Revoked = true });
    public void SignalStop() => DurableFiles.Write(Path.Combine(directory, "stopped.json"), new { Stopped = true });
    public void Revoke(string runId) => Guard(() =>
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        DurableFiles.Write(Path.Combine(directory, "revoked-" + DurableFiles.Hash(runId) + ".json"), new
        {
            Revoked = true
        });
        DurableSnapshot current = Load();
        if (current.Domain.Sessions.TryGetValue(runId, out StoredSession? s))
        {
            if (s.Revoked)
            {
                return true;
            }

            current.Domain.Sessions[runId] = s with
            {
                Revoked = true
            };
        }

        Commit(current, current.Domain, "run_revoked", runId, null, new(Outcome.Allowed, "run_revoked"));
        return true;
    });
    public void Stop() => Guard(() =>
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        DurableFiles.Write(Path.Combine(directory, "stopped.json"), new
        {
            Stopped = true
        });
        DurableSnapshot current = Load();
        if (current.Domain.Stopped)
        {
            return true;
        }

        current.Domain.Stopped = true;
        current.Domain.Approvals.Clear();
        Commit(current, current.Domain, "gateway_stopped", null, null, new(Outcome.Allowed, "gateway_stopped"));
        return true;
    });
    private static Decision Deny(string reason) => new(Outcome.Denied, reason);
}
