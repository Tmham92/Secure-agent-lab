using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;

namespace SecureAgentLab.Core.Documents;

public sealed class DocumentExecutor(IDocumentReader reader) : IToolExecutor
{
    private readonly SyntheticExecutor publication = new();
    private int reads;
    public Decision Execute(Proposal proposal, long remainingBytes)
    {
        if (proposal.Operation != Operation.ReadDocument)
        {
            return publication.Execute(proposal, remainingBytes);
        }

        if (proposal.Content is not null || proposal.ExpectedVersion is not null)
        {
            return new(Outcome.Denied, "malformed_proposal");
        }

        Decision result = reader.Read(proposal.Resource, remainingBytes);
        if (result.Outcome == Outcome.Allowed)
        {
            Interlocked.Increment(ref reads);
        }

        return result;
    }

    public MockEffects GetEffects() => new(Volatile.Read(ref reads), publication.GetEffects().Publications);
}
