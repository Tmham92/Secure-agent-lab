using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Core.Grants;

public sealed record ApprovalState(string RunId, Proposal Proposal, DateTimeOffset ExpiresAt, bool Consumed);
