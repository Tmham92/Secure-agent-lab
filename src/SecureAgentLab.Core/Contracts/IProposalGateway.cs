namespace SecureAgentLab.Core.Contracts;
/// <summary>Only this proposal surface is passed to the deterministic worker.
/// Interface visibility is a design convention, not a hostile-code boundary.</summary>
public interface IProposalGateway
{
    Decision Execute(string? runId, Proposal? proposal, string? approvalTicket = null);
}
