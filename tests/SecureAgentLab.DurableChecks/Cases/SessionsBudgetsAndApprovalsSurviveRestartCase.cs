using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class SessionsBudgetsAndApprovalsSurviveRestartCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, global::SecureAgentLab.Core.Contracts.Proposal read, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        CheckAssertions.Equal(Outcome.Allowed, g.Execute(run, read).Outcome);
        g = f.Open();
        CheckAssertions.Equal(29, g.GetSession(run)!.RemainingCalls);
        CheckAssertions.Equal("publication_approved", g.Execute(run, draft, ticket, "once").Reason);
        g = f.Open();
        CheckAssertions.Equal(new MockEffects(1, 1), g.GetEffects());
        CheckAssertions.Equal("publication_replayed", g.Execute(run, draft, ticket, "once").Reason);
        CheckAssertions.Equal("approval_consumed", g.Execute(run, draft, ticket, "different").Reason);
        CheckAssertions.Equal(new MockEffects(1, 1), g.GetEffects());
    }
}
