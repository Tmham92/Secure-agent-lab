using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class ReadProposalCannotSupplyAlternateContentCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        Gateway g = GatewayFor(Fixture());
        string run = Grant(g, "documents/task");
        CheckAssertions.Equal(Outcome.Denied, g.Execute(run, read with
        {
            Content = "forged content"
        }).Outcome);
        CheckAssertions.Equal(0, g.GetEffects().DocumentReads);
    }
}
