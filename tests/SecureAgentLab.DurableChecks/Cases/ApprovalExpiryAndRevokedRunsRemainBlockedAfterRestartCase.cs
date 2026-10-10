using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class ApprovalExpiryAndRevokedRunsRemainBlockedAfterRestartCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromSeconds(1));
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        g = f.Open();
        CheckAssertions.Equal("approval_expired", g.Execute(run, draft, ticket, "key").Reason);
        g.Revoke(run);
        CheckAssertions.Equal("identity_revoked", f.Open().Execute(run, draft, ticket, "key").Reason);
        CheckAssertions.Equal(new MockEffects(0, 0), g.GetEffects());
    }
}
