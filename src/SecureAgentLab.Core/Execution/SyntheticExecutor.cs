using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Policy;

namespace SecureAgentLab.Core.Execution;

public sealed class SyntheticExecutor : IToolExecutor
{
    private int _reads;
    private int _publications;
    public Decision Execute(Proposal proposal, long remainingBytes)
    {
        // Revalidate at the executor: a policy implementation cannot enable a generic tool.
        if (!DefaultPolicy.IsSupported(proposal.Operation, proposal.Resource) || proposal.Resource == "documents/reference" || proposal.ExpectedVersion is not null)
        {
            return new(Outcome.Denied, "executor_out_of_scope");
        }

        bool publish = proposal.Operation == Operation.PublishReport;
        string result = publish ? Gateway.PublicationResult : Gateway.TaskDocument;
        if (System.Text.Encoding.UTF8.GetByteCount(result) > remainingBytes)
        {
            return new(Outcome.Denied, "response_budget_exhausted");
        }

        if (publish)
        {
            Interlocked.Increment(ref _publications);
        }
        else
        {
            Interlocked.Increment(ref _reads);
        }

        return new(Outcome.Allowed, publish ? "publication_approved" : "scoped_read", result);
    }

    public MockEffects GetEffects() => new(Volatile.Read(ref _reads), Volatile.Read(ref _publications));
}
