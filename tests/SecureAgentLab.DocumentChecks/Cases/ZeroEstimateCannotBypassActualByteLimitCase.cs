using System.Text;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class ZeroEstimateCannotBypassActualByteLimitCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        Gateway g = GatewayFor(Fixture());
        string run = Grant(g, "documents/task", Encoding.UTF8.GetByteCount(Gateway.TaskDocument) - 1);
        CheckAssertions.Equal("response_budget_exhausted", g.Execute(run, read).Reason);
        CheckAssertions.Equal(0, g.GetEffects().DocumentReads);
    }
}
