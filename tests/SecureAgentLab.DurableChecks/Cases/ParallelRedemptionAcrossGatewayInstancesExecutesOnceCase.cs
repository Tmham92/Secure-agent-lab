using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class ParallelRedemptionAcrossGatewayInstancesExecutesOnceCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        DurableGateway other = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        var results = new Decision[12];
        Parallel.For(0, results.Length, i => results[i] = (i % 2 == 0 ? g : other).Execute(run, draft, ticket, "key-" + i));
        CheckAssertions.Equal(1, results.Count(d => d.Outcome == Outcome.Allowed));
        CheckAssertions.Equal(11, results.Count(d => d.Reason == "approval_consumed"));
        CheckAssertions.Equal(new MockEffects(0, 1), f.Open().GetEffects());
    }
}
