using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Recovery;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class AuditOutageRejectsWritesBeforeEffectsOrApprovalConsumptionCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        f.Sink.Offline = true;
        CheckAssertions.Equal("authorization_dependency_unavailable", g.Execute(run, draft, ticket, "once").Reason);
        CheckAssertions.Throws<AuthorizationDependencyException>(() => g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1)));
        f.Sink.Offline = false;
        CheckAssertions.Equal(new MockEffects(0, 0), f.Open().GetEffects());
        CheckAssertions.Equal("publication_approved", g.Execute(run, draft, ticket, "once").Reason);
    }
}
