using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class SharedDurableBudgetRejectsResetRetryFanoutAndSurvivesRestartCase
{
    internal static void Run(Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        string path = Path.Combine(f.State, "budget");
        var b = new DurableBudgetStore(path, f.IntegrityKey, f.Clock);
        b.Create(run, new(f.Clock.GetUtcNow().AddMinutes(1), 10, 10, 10, 10));
        Phase7Checks.Throws<InvalidOperationException>(() => b.Create(run, new(f.Clock.GetUtcNow().AddMinutes(1), 10, 10, 10, 10)));
        Phase7Checks.Equal<string?>(null, b.Reserve(run, new(2, 2, 2), out string? held));
        var other = new DurableBudgetStore(path, f.IntegrityKey, f.Clock);
        Phase7Checks.Equal("runtime_fanout", other.Reserve(run, new(1, 1, 1), out _));
        b.Release(run, held!);
        Phase7Checks.Equal("runtime_retry_budget", other.Reserve(run, new(1, 1, 1, 1), out _));
        Phase7Checks.Equal(8L, other.Snapshot(run).CostMicros);
        Phase7Checks.Equal(7, other.Snapshot(run).Attempts);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Phase7Checks.Equal("runtime_deadline", other.Reserve(run, new(0, 0, 0), out _));
    }
}
