using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Recovery;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class BudgetTamperingAndMissingStateFailClosedCase
{
    internal static void Run(Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        string path = Path.Combine(f.State, "budget");
        var b = new DurableBudgetStore(path, f.IntegrityKey, f.Clock);
        b.Create(run, new(f.Clock.GetUtcNow().AddMinutes(1), 10, 10, 10, 10));
        File.WriteAllText(Path.Combine(path, "budgets.json"), "{}");
        Phase7Checks.Throws<AuthorizationDependencyException>(() => b.Snapshot(run));
        File.Delete(Path.Combine(path, "budgets.json"));
        Phase7Checks.Throws<AuthorizationDependencyException>(() => b.Reserve(run, new(1, 1, 1), out _));
    }
}
