using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class PublicationRecoversExactlyOnceAfterPointCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, global::SecureAgentLab.Durable.Models.DurabilityPoint point, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        DurableGateway broken = f.Open(crash: observed =>
        {
            if (observed == point)
            {
                throw new SimulatedCrash();
            }
        });
        CheckAssertions.Throws<SimulatedCrash>(() => broken.Execute(run, draft, ticket, "once"));
        DurableGateway recovered = f.Open();
        CheckAssertions.Equal(new MockEffects(0, 1), recovered.GetEffects());
        CheckAssertions.Equal("publication_replayed", recovered.Execute(run, draft, ticket, "once").Reason);
        CheckAssertions.Equal(new MockEffects(0, 1), recovered.GetEffects());
        CheckAssertions.Assert(!File.Exists(Path.Combine(f.State, "pending.json")), "Pending transaction not reconciled");
    }
}
