using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.TransportChecks.Fixtures;
namespace SecureAgentLab.TransportChecks.Cases;

internal static class GrantQuotaRemainsAtomicUnderConcurrencyCase
{
    internal static Task Run(global::SecureAgentLab.TransportChecks.Fixtures.TestClock clock, global::SecureAgentLab.Core.Contracts.Proposal read)
    {
        var g = new Gateway(clock);
        var grant = new TaskGrant("v1", clock.GetUtcNow().AddMinutes(2), 7, 100000, [new(Operation.ReadDocument, "documents/task")]);
        string run = g.CreateSession(grant);
        var outcomes = new Decision[100];
        Parallel.For(0, 100, i => outcomes[i] = g.Execute(run, read));
        CheckAssertions.Equal(7, outcomes.Count(d => d.Outcome == Outcome.Allowed));
        CheckAssertions.Equal(new MockEffects(7, 0), g.GetEffects());
        return Task.CompletedTask;
    }
}
