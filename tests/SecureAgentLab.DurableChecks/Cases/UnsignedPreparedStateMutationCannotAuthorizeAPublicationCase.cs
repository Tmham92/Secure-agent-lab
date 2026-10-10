using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Persistence;
using SecureAgentLab.Durable.Recovery;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class UnsignedPreparedStateMutationCannotAuthorizeAPublicationCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        DurableGateway broken = f.Open(crash: point =>
        {
            if (point == DurabilityPoint.AfterPrepared)
            {
                throw new SimulatedCrash();
            }
        });
        CheckAssertions.Throws<SimulatedCrash>(() => broken.Execute(run, draft, ticket, "once"));
        string path = Path.Combine(f.State, "pending.json");
        SealedChange pending = DurableFiles.Read<SealedChange>(path);
        pending.Change.Domain.Reads = 900;
        DurableFiles.Write(path, pending);
        CheckAssertions.Throws<AuthorizationDependencyException>(() => f.Open());
    }
}
