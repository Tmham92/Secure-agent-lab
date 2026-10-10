using System.Text;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class ExactFileReadAndMeasuredMultibyteResponseQuotaCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        string root = Fixture();
        Gateway g = GatewayFor(root);
        int bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
        string run = Grant(g, "documents/task", bytes);
        CheckAssertions.Equal(Gateway.TaskDocument, g.Execute(run, read).Result);
        CheckAssertions.Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        CheckAssertions.Equal("response_budget_exhausted", g.Execute(run, read).Reason);
        CheckAssertions.Equal(new MockEffects(1, 0), g.GetEffects());
    }
}
