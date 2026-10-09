namespace SecureAgentLab.Core;

public enum Operation { ReadDocument, PublishReport, ExternalRequest, MessageAgent, ChangePermissions }
public enum Outcome { Allowed, Denied, ApprovalRequired, RecoveryRequired }
public sealed record Proposal(Operation Operation, string Resource, long? EstimatedBytes = null, string? Content = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? ExpectedVersion = null);
public sealed record Decision(Outcome Outcome, string Reason, string? Result = null);
public sealed record AuditEntry(long Sequence, DateTimeOffset UtcTime, string? RunId,
    Proposal? Proposal, Outcome Outcome, string Reason, string PreviousHash, string Hash, string EventType = "execution", string? PolicyVersion = null,
    string? ContentHash = null, string? ApprovalReference = null, string? CorrelationId = null,
    long ResponseBytes = 0, string? StateDigest = null);
public sealed record SessionSnapshot(DateTimeOffset ExpiresAt, int RemainingCalls,
    long RemainingResponseBytes, bool Revoked);
public sealed record MockEffects(int DocumentReads, int Publications);

/// <summary>Only this proposal surface is passed to the deterministic worker.
/// Interface visibility is a design convention, not a hostile-code boundary.</summary>
public interface IProposalGateway
{
    Decision Execute(string? runId, Proposal? proposal, string? approvalTicket = null);
}

public interface ILabGateway : IProposalGateway, IRunBudgetStore, IAuditSink
{
    string CreateSession(TaskGrant grant);
    TaskGrant? GetGrant(string runId);
    string ApprovePublication(string runId, Proposal proposal, TimeSpan lifetime);
    Decision Execute(string? runId, Proposal? proposal, string? approvalTicket, string? idempotencyKey);
    void Revoke(string runId);
    void Stop();
    MockEffects GetEffects();
}
