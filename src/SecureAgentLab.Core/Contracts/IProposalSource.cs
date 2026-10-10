namespace SecureAgentLab.Core.Contracts;

public interface IProposalSource
{
    IEnumerable<Proposal> GetProposals();
}
