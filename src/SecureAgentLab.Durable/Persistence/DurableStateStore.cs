using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Policy;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Recovery;
using static global::SecureAgentLab.Durable.Approvals.PublicationBinding;

namespace SecureAgentLab.Durable.Persistence;
// Caller owns DurableFiles.Lock for the entire authorization/effect/audit transaction.
// This component performs persistence and reconciliation; it never acquires a separate lock.
internal sealed class DurableStateStore
{
    private readonly string _statePath;
    private readonly string _pendingPath;
    private readonly IAuditCollector _collector;
    private readonly CheckpointVerifier _verifier;
    private readonly byte[] _integrityKey;
    private readonly string _policyVersion;
    private readonly TimeProvider _clock;
    private readonly Action<DurabilityPoint>? _crash;
    internal DurableStateStore(string directory, IAuditCollector collector, CheckpointVerifier verifier, byte[] integrityKey, string policyVersion, TimeProvider clock, Action<DurabilityPoint>? crash)
    {
        _statePath = Path.Combine(directory, "state.json");
        _pendingPath = Path.Combine(directory, "pending.json");
        _collector = collector;
        _verifier = verifier;
        _integrityKey = integrityKey.ToArray();
        _policyVersion = policyVersion;
        _clock = clock;
        _crash = crash;
    }

    internal bool HasPendingChange => File.Exists(_pendingPath);

    private string Seal(PreparedChange change) => Convert.ToHexString(HMACSHA256.HashData(_integrityKey, JsonSerializer.SerializeToUtf8Bytes(change)));
    private bool Same(SignedCheckpoint a, SignedCheckpoint b) => a.Value == b.Value;
    private void Validate(DurableSnapshot snapshot)
    {
        if (snapshot.Domain is null || snapshot.Checkpoint is null || snapshot.Domain.Sessions is null || snapshot.Domain.Approvals is null || snapshot.Domain.Publications is null)
        {
            throw new AuthorizationDependencyException("invalid_state");
        }

        _verifier.Verify(snapshot.Checkpoint);
        string? expected = snapshot.Checkpoint.Value.Sequence == 0 ? new DomainState().Digest() : snapshot.Checkpoint.Value.StateDigest;
        if (snapshot.Domain.Digest() != expected)
        {
            throw new AuthorizationDependencyException("state_tampered");
        }
    }

    internal DurableSnapshot Load()
    {
        SignedCheckpoint head = _collector.GetCheckpoint();
        _verifier.Verify(head);
        DurableSnapshot snapshot;
        if (!File.Exists(_statePath))
        {
            if (head.Value.Sequence != 0 || File.Exists(_pendingPath))
            {
                throw new AuthorizationDependencyException("state_missing");
            }

            snapshot = new(new DomainState(), head);
            DurableFiles.Write(_statePath, snapshot);
        }
        else
        {
            snapshot = DurableFiles.Read<DurableSnapshot>(_statePath);
        }

        Validate(snapshot);
        if (File.Exists(_pendingPath))
        {
            SealedChange pending = DurableFiles.Read<SealedChange>(_pendingPath);
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
            _verifier.Verify(change.Previous);
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

                File.Delete(_pendingPath);
                return snapshot;
            }

            if (!Same(snapshot.Checkpoint, change.Previous))
            {
                throw new AuthorizationDependencyException("pending_conflict");
            }

            if (!Same(head, change.Previous) && (head.Value.Sequence != change.Event.Sequence || head.Value.Hash != change.Event.Hash))
            {
                throw new AuthorizationDependencyException("audit_head_mismatch");
            }

            SignedCheckpoint acknowledged = _collector.Append(change.Event);
            _verifier.Verify(acknowledged);
            if (acknowledged.Value.Sequence != change.Event.Sequence || acknowledged.Value.Hash != change.Event.Hash || acknowledged.Value.StateDigest != change.Domain.Digest())
            {
                throw new AuthorizationDependencyException("audit_ack_invalid");
            }

            snapshot = new(change.Domain, acknowledged);
            DurableFiles.Write(_statePath, snapshot);
            File.Delete(_pendingPath);
        }
        else if (!Same(snapshot.Checkpoint, head))
        {
            throw new AuthorizationDependencyException("audit_head_mismatch");
        }

        return snapshot;
    }

    internal DurableSnapshot Commit(DurableSnapshot current, DomainState domain, string type, string? run, Proposal? proposal, Decision decision, string? approval = null)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        if (now < current.Checkpoint.Value.UtcTime)
        {
            throw new AuthorizationDependencyException("clock_regressed");
        }

        // Only identifiers and hashes reach the collector. Arbitrary strings and content do not.
        Proposal? safeProposal = proposal is null ? null : new(proposal.Operation, DefaultPolicy.IsSupported(proposal.Operation, proposal.Resource) ? proposal.Resource : "sha256:" + DurableFiles.Hash(proposal.Resource ?? ""), proposal.EstimatedBytes, ExpectedVersion: proposal.ExpectedVersion);
        var entry = new AuditEntry(current.Checkpoint.Value.Sequence + 1, now, run is null ? null : DurableFiles.Hash(run), safeProposal, decision.Outcome, decision.Reason, current.Checkpoint.Value.Hash, "", type, _policyVersion, proposal?.Operation == Operation.PublishReport ? ContentHash(proposal) : proposal?.Operation == Operation.ReadDocument && decision.Result is not null ? SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(decision.Result)) : null, approval, Guid.NewGuid().ToString("N"), decision.Result is null ? 0 : Encoding.UTF8.GetByteCount(decision.Result), domain.Digest());
        entry = entry with
        {
            Hash = AuditChain.ComputeHash(entry)
        };
        var change = new PreparedChange(domain, current.Checkpoint, entry);
        DurableFiles.Write(_pendingPath, new SealedChange(change, Seal(change)));
        _crash?.Invoke(DurabilityPoint.AfterPrepared);
        SignedCheckpoint acknowledged = _collector.Append(entry);
        _verifier.Verify(acknowledged);
        if (acknowledged.Value.Sequence != entry.Sequence || acknowledged.Value.Hash != entry.Hash || acknowledged.Value.StateDigest != domain.Digest())
        {
            throw new AuthorizationDependencyException("audit_ack_invalid");
        }

        _crash?.Invoke(DurabilityPoint.AfterAuditAcknowledged);
        var next = new DurableSnapshot(domain, acknowledged);
        DurableFiles.Write(_statePath, next);
        _crash?.Invoke(DurabilityPoint.AfterStateCommitted);
        File.Delete(_pendingPath);
        return next;
    }
}
