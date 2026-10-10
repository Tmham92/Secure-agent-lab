using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class ClockRegressionAndByteLimitFailuresNeverPublishCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = g.CreateSession(TaskGrant.Default(f.Clock.GetUtcNow().AddMinutes(5), 10, 0));
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        CheckAssertions.Equal("response_budget_exhausted", g.Execute(run, draft, ticket, "once").Reason);
        f.Clock.Advance(TimeSpan.FromSeconds(-1));
        CheckAssertions.Equal("authorization_dependency_unavailable", g.Execute(run, draft, ticket, "once").Reason);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        CheckAssertions.Equal(new MockEffects(0, 0), g.GetEffects());
    }
}
