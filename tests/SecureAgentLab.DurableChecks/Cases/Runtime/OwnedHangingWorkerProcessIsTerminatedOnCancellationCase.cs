using System.Diagnostics;
using System.Reflection;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class OwnedHangingWorkerProcessIsTerminatedOnCancellationCase
{
    internal static async Task RunAsync(Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        DurableBudgetStore b = Phase7Checks.Budget(f, run);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--hang-fixture");
        using Process child = Process.Start(start)!;
        try
        {
            Phase7Checks.Equal("READY", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var controller = new BoundedRunController(g, b, run, TimeSpan.FromMilliseconds(150));
            Phase7Checks.Equal(Outcome.RecoveryRequired, (await controller.RunAsync(new(1, 1, 1), async ct =>
                        {
                            try
                            {
                                await child.WaitForExitAsync(ct);
                                return new(Outcome.Denied, "fixture_exited");
                            }
                            finally
                            {
                                if (!child.HasExited)
                                {
                                    child.Kill(entireProcessTree: true);
                                }

                                await child.WaitForExitAsync();
                            }
                        })).Outcome);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Phase7Checks.Assert(child.HasExited, "Worker survived cancellation");
            Phase7Checks.Equal(new MockEffects(0, 0), g.GetEffects());
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
    }
}
