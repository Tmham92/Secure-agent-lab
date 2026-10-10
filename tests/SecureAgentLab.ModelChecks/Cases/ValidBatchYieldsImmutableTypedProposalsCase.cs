using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class ValidBatchYieldsImmutableTypedProposalsCase
{
    internal static void Run(string Valid, BytesCallback Bytes)
    {
        var source = new ModelProposalSource(Bytes(Valid));
        CheckAssertions.Equal(new Proposal(Operation.ReadDocument, "documents/task"), source.GetProposals().Single());
    }
}
