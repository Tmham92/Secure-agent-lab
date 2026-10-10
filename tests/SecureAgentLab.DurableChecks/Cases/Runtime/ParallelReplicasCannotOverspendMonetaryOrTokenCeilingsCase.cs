using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class ParallelReplicasCannotOverspendMonetaryOrTokenCeilingsCase
{
    internal static void Run(Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        string path = Path.Combine(f.State, "budget");
        var b = new DurableBudgetStore(path, f.IntegrityKey, f.Clock);
        b.Create(run, new(f.Clock.GetUtcNow().AddMinutes(1), 100, 10, 10, 10, 4));
        int allowed = 0;
        Parallel.For(0, 50, _ =>
        {
            var other = new DurableBudgetStore(path, f.IntegrityKey, f.Clock);
            if (other.Reserve(run, new(1, 1, 1), out string? lease) is null)
            {
                Interlocked.Increment(ref allowed);
                other.Release(run, lease!);
            }
        });
        Phase7Checks.Assert(allowed > 0 && allowed <= 10, "No valid admission or overspent quota");
        Phase7Checks.Equal(10L - allowed, b.Snapshot(run).CostMicros);
        Phase7Checks.Equal(10L - allowed, b.Snapshot(run).InputTokens);
        Phase7Checks.Equal(0, b.Snapshot(run).Active);
        Phase7Checks.Equal("runtime_cost_budget", b.Reserve(run, new(0, 0, 11), out _));
        Phase7Checks.Equal("runtime_token_budget", b.Reserve(run, new(11, 0, 0), out _));
    }
}
