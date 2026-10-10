using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Transport.Requests;

public sealed record ApprovalRequest(Proposal Proposal, int LifetimeSeconds = 60);
