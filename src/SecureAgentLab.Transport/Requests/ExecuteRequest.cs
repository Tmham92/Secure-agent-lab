using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Transport.Requests;

public sealed record ExecuteRequest(Proposal Proposal, string? ApprovalTicket = null, string? IdempotencyKey = null);
