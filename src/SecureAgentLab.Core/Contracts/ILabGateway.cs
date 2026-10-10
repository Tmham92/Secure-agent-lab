using SecureAgentLab.Core.Grants;

namespace SecureAgentLab.Core.Contracts;

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
