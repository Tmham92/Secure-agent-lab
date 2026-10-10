using System.Diagnostics;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class HangingCooperativeToolTimesOutRevokesRunAndCreatesNoEffectCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Contracts.Proposal draft, Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        DurableBudgetStore b = Phase7Checks.Budget(f, run);
        var controller = new BoundedRunController(g, b, run, TimeSpan.FromMilliseconds(150));
        var timer = Stopwatch.StartNew();
        Decision result = await controller.RunAsync(new(1, 1, 1), async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return g.Execute(run, draft);
        });
        Phase7Checks.Equal(Outcome.RecoveryRequired, result.Outcome);
        Phase7Checks.Assert(timer.Elapsed < TimeSpan.FromSeconds(3), "Containment too slow");
        Phase7Checks.Equal("tool_timeout", b.Snapshot(run).LastDenial);
        Phase7Checks.Equal(new MockEffects(0, 0), g.GetEffects());
        Phase7Checks.Equal("identity_revoked", g.Execute(run, new(Operation.ReadDocument, "documents/task")).Reason);
    }
}
