using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class NoncooperativeLateToolRemainsQuarantinedAndItsLaterProposalIsDeniedCase
{
    internal static async Task RunAsync(Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        DurableBudgetStore b = Phase7Checks.Budget(f, run);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<Decision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new BoundedRunController(g, b, run, TimeSpan.FromMilliseconds(150));
        Phase7Checks.Equal(Outcome.RecoveryRequired, (await controller.RunAsync(new(1, 1, 1), async _ =>
                {
                    await release.Task;
                    Decision d = g.Execute(run, new(Operation.ReadDocument, "documents/task"));
                    finished.SetResult(d);
                    return d;
                })).Outcome);
        Phase7Checks.Equal(1, b.Snapshot(run).Active);
        release.SetResult();
        Phase7Checks.Equal("identity_revoked", (await finished.Task.WaitAsync(TimeSpan.FromSeconds(3))).Reason);
        Phase7Checks.Equal(new MockEffects(0, 0), g.GetEffects());
    }
}
