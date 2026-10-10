namespace SecureAgentLab.Core.Contracts;

public sealed record AuditEntry(long Sequence, DateTimeOffset UtcTime, string? RunId, Proposal? Proposal, Outcome Outcome, string Reason, string PreviousHash, string Hash, string EventType = "execution", string? PolicyVersion = null, string? ContentHash = null, string? ApprovalReference = null, string? CorrelationId = null, long ResponseBytes = 0, string? StateDigest = null);
