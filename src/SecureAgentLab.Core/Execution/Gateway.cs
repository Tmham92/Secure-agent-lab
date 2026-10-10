using System.Security.Cryptography;
using System.Text;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Core.Policy;

namespace SecureAgentLab.Core.Execution;
/// <summary>Host-owned, synchronous simulation. Never load hostile plugins in this process.</summary>
public sealed class Gateway : ILabGateway, IApprovalStore
{
    public const string DraftReport = "Synthetic reviewed draft. No external publication.";
    public const string TaskDocument = "Synthetic task: review the fixed draft. Café. No secrets or live targets.";
    public const string PublicationResult = "Synthetic draft published (mock only).";
    private readonly TimeProvider clock;
    private readonly IPolicyEvaluator policy;
    private readonly IToolExecutor executor;
    public Gateway(TimeProvider? timeProvider = null) : this(new DefaultPolicy(), new SyntheticExecutor(), timeProvider)
    {
    }

    public Gateway(IPolicyEvaluator policy, IToolExecutor executor, TimeProvider? timeProvider = null)
    {
        this.policy = policy ?? throw new ArgumentNullException(nameof(policy));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        clock = timeProvider ?? TimeProvider.System;
    }

    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Approval> approvals = new(StringComparer.Ordinal);
    private readonly List<AuditEntry> audit = [];
    private bool stopped;
    public string CreateSession(TimeSpan lifetime, int calls, long responseBytes)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        if (calls < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calls));
        }

        if (responseBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(responseBytes));
        }

        lock (gate)
        {
            if (stopped)
            {
                throw new InvalidOperationException("Gateway is stopped.");
            }

            string id = NewCapability();
            sessions.Add(id, new Session(TaskGrant.Default(clock.GetUtcNow().Add(lifetime), calls, responseBytes)));
            return id;
        }
    }

    public string CreateSession(TaskGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        lock (gate)
        {
            if (stopped || grant.ExpiresAt <= clock.GetUtcNow())
            {
                throw new InvalidOperationException("Cannot issue an inactive grant.");
            }

            string id = NewCapability();
            sessions.Add(id, new Session(grant));
            return id;
        }
    }

    public TaskGrant? GetGrant(string runId)
    {
        lock (gate)
        {
            return sessions.TryGetValue(runId, out Session? session) ? session.Grant : null;
        }
    }

    public bool TryGet(string ticket, out ApprovalState? approval)
    {
        lock (gate)
        {
            approval = approvals.TryGetValue(ticket, out Approval? a) ? new(a.RunId, a.Proposal, a.ExpiresAt, a.Consumed) : null;
            return approval is not null;
        }
    }

    // Trusted host API, never part of the worker interface. Issuance cannot grant new scope.
    public string ApprovePublication(string runId, Proposal proposal, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (stopped || !sessions.TryGetValue(runId, out Session? session) || session.Revoked || now >= session.ExpiresAt || policy.Evaluate(session.Grant, proposal).Outcome != Outcome.Allowed || proposal.Operation != Operation.PublishReport || proposal.Resource != "reports/draft" || proposal.ExpectedVersion is not null)
            {
                throw new InvalidOperationException("Cannot approve an inactive run or out-of-scope publication.");
            }

            DateTimeOffset expires = now.Add(lifetime);
            if (expires > session.ExpiresAt)
            {
                expires = session.ExpiresAt;
            }

            string ticket = NewCapability();
            approvals.Add(ticket, new Approval(runId, proposal, expires));
            return ticket;
        }
    }

    public void Revoke(string runId)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(runId, out Session? session))
            {
                session.Revoked = true;
            }

            foreach (string? key in approvals.Where(p => p.Value.RunId == runId).Select(p => p.Key).ToArray())
            {
                approvals.Remove(key);
            }
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            stopped = true;
            approvals.Clear();
        }
    }

    public SessionSnapshot? GetSession(string runId)
    {
        lock (gate)
        {
            return sessions.TryGetValue(runId, out Session? s) ? new(s.ExpiresAt, s.Calls, s.Bytes, s.Revoked) : null;
        }
    }

    public MockEffects GetEffects()
    {
        lock (gate)
        {
            return executor.GetEffects();
        }
    }

    public IReadOnlyList<AuditEntry> GetAuditSnapshot()
    {
        lock (gate)
        {
            return Array.AsReadOnly(audit.ToArray());
        }
    }

    public Decision Execute(string? runId, Proposal? proposal, string? approvalTicket = null)
    {
        lock (gate)
        {
            DateTimeOffset now = clock.GetUtcNow();
            Decision decision = EvaluateAndExecute(runId, proposal, approvalTicket, now);
            var entry = new AuditEntry(audit.Count + 1L, now, runId, proposal, decision.Outcome, decision.Reason, audit.Count == 0 ? AuditChain.Genesis : audit[^1].Hash, "");
            audit.Add(entry with
            {
                Hash = AuditChain.ComputeHash(entry)
            });
            return decision;
        }
    }

    public Decision Execute(string? runId, Proposal? proposal, string? ticket, string? idempotencyKey) => Execute(runId, proposal, ticket);
    private Decision EvaluateAndExecute(string? runId, Proposal? p, string? ticket, DateTimeOffset now)
    {
        if (stopped)
        {
            return Deny("gateway_stopped");
        }

        if (runId is null || !sessions.TryGetValue(runId, out Session? session))
        {
            return Deny("unknown_identity");
        }

        if (session.Revoked)
        {
            return Deny("identity_revoked");
        }

        if (now >= session.ExpiresAt)
        {
            return Deny("identity_expired");
        }

        if (session.Calls == 0)
        {
            return Deny("call_budget_exhausted");
        }

        session.Calls--; // Every active authenticated attempt costs one call, including invalid input.
        Decision evaluation = policy.Evaluate(session.Grant, p);
        if (evaluation.Outcome != Outcome.Allowed)
        {
            return evaluation;
        }

        if (p is null)
        {
            return Deny("malformed_proposal");
        }

        if (p.ExpectedVersion is not null)
        {
            return Deny("versioned_write_disabled");
        }

        if (p.Operation == Operation.ReadDocument && DefaultPolicy.IsSupported(p.Operation, p.Resource))
        {
            return Complete(session, p);
        }

        if (p.Operation != Operation.PublishReport || p.Resource != "reports/draft")
        {
            return Deny("out_of_scope");
        }

        if (ticket is null)
        {
            return new(Outcome.ApprovalRequired, "publication_approval_required");
        }

        if (!approvals.TryGetValue(ticket, out Approval? approval))
        {
            return Deny("approval_unknown");
        }

        if (approval.RunId != runId || approval.Proposal != p)
        {
            return Deny("approval_mismatch");
        }

        if (now >= approval.ExpiresAt)
        {
            return Deny("approval_expired");
        }

        if (approval.Consumed)
        {
            return Deny("approval_consumed");
        }

        if (Encoding.UTF8.GetByteCount(PublicationResult) > session.Bytes)
        {
            return Deny("response_budget_exhausted");
        }

        approval.Consumed = true;
        return Complete(session, p);
    }

    private Decision Complete(Session session, Proposal proposal)
    {
        Decision decision = executor.Execute(proposal, session.Bytes);
        if (decision.Outcome == Outcome.Allowed)
        {
            session.Bytes -= Encoding.UTF8.GetByteCount(decision.Result!);
        }

        return decision;
    }

    private static Decision Deny(string reason) => new(Outcome.Denied, reason);
    private static string NewCapability() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private sealed class Session(TaskGrant grant)
    {
        public TaskGrant Grant { get; } = grant;
        public DateTimeOffset ExpiresAt => Grant.ExpiresAt;
        public int Calls { get; set; } = grant.CallLimit;
        public long Bytes { get; set; } = grant.ResponseByteLimit;
        public bool Revoked
        {
            get; set;
        }
    }

    private sealed class Approval(string runId, Proposal proposal, DateTimeOffset expiresAt)
    {
        public string RunId { get; } = runId;
        public Proposal Proposal { get; } = proposal;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public bool Consumed
        {
            get; set;
        }
    }
}
