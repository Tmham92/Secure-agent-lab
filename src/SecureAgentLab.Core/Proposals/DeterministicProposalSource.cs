using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Core.Proposals;

public sealed class DeterministicProposalSource : IProposalSource
{
    public IEnumerable<Proposal> GetProposals() => [new(Operation.ReadDocument, "documents/task"), new(Operation.ExternalRequest, "https://synthetic.invalid"), new(Operation.MessageAgent, "shared-cache/message-board"), new(Operation.ChangePermissions, "grants/admin"), new(Operation.PublishReport, "reports/draft", 0)];
}
