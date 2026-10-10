using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class VersionedWriteRecoversExactlyOnceAfterPointCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, global::SecureAgentLab.Durable.Models.DurabilityPoint point, Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        DurableGateway crashing = Phase7Checks.Open(f, p =>
        {
            if (p == point)
            {
                throw new SimulatedCrash();
            }
        });
        Phase7Checks.Throws<SimulatedCrash>(() => crashing.Execute(run, draft, ticket, "crash"));
        g = Phase7Checks.Open(f);
        Phase7Checks.Equal(new ReportSnapshot(1, draft.Content), g.GetReport());
        Phase7Checks.Equal("publication_replayed", g.Execute(run, draft, ticket, "crash").Reason);
        Phase7Checks.Equal(new MockEffects(0, 1), g.GetEffects());
    }
}
