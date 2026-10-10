using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class ReplicaRevocationAndGlobalStopCancelActiveWorkCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Contracts.Proposal draft, Func<Fixture> Make)
    {
        foreach (bool stop in new[]
        {
            false,
            true
        }

        )
        {
            Fixture f = Make();
            DurableGateway g = Phase7Checks.Open(f);
            string run = f.Run(g);
            DurableBudgetStore b = Phase7Checks.Budget(f, run);
            var controller = new BoundedRunController(g, b, run, TimeSpan.FromSeconds(2));
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<Decision> work = controller.RunAsync(new(1, 1, 1), async ct =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return g.Execute(run, draft);
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var other = new BoundedRunController(Phase7Checks.Open(f), new DurableBudgetStore(Path.Combine(f.State, "budget"), f.IntegrityKey, f.Clock), run, TimeSpan.FromSeconds(2));
            if (stop)
            {
                other.Stop();
            }
            else
            {
                other.Revoke();
            }
            Phase7Checks.
                        Equal(Outcome.RecoveryRequired, (await work).Outcome);
            Phase7Checks.Equal(new MockEffects(0, 0), g.GetEffects());
        }
    }
}
