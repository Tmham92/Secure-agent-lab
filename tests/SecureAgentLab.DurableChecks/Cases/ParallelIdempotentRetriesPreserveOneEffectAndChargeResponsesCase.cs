using System.Text;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class ParallelIdempotentRetriesPreserveOneEffectAndChargeResponsesCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        var results = new Decision[12];
        Parallel.For(0, results.Length, i => results[i] = g.Execute(run, draft, ticket, "same"));
        CheckAssertions.Equal(1, results.Count(d => d.Reason == "publication_approved"));
        CheckAssertions.Equal(11, results.Count(d => d.Reason == "publication_replayed"));
        CheckAssertions.Equal(18, g.GetSession(run)!.RemainingCalls);
        CheckAssertions.Equal(4096L - Encoding.UTF8.GetByteCount(Gateway.PublicationResult) * 12, g.GetSession(run)!.RemainingResponseBytes);
        CheckAssertions.Equal(new MockEffects(0, 1), g.GetEffects());
        string nextTicket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        CheckAssertions.Equal("idempotency_conflict", g.Execute(run, draft, nextTicket, "same").Reason);
    }
}
