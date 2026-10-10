using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class PolicyVersionChangeInvalidatesOutstandingGrantsAndApprovalsCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        g = f.Open(version: "synthetic-v2");
        CheckAssertions.Equal("policy_version_changed", g.Execute(run, draft, ticket, "key").Reason);
        CheckAssertions.Equal(new MockEffects(0, 0), g.GetEffects());
        CheckAssertions.Throws<InvalidOperationException>(() => g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1)));
    }
}
