using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class LostAcknowledgementReportsRecoveryRequiredAndReconcilesOnceCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        f.Sink.LoseAcknowledgement = true;
        CheckAssertions.Equal(Outcome.RecoveryRequired, g.Execute(run, draft, ticket, "once").Outcome);
        f.Sink.LoseAcknowledgement = false;
        CheckAssertions.Equal(new MockEffects(0, 1), f.Open().GetEffects());
        CheckAssertions.Equal("publication_replayed", g.Execute(run, draft, ticket, "once").Reason);
    }
}
