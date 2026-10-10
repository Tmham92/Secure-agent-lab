using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Durable.Execution;

public sealed class BoundedRunController
{
    private readonly DurableGateway gateway;
    private readonly DurableBudgetStore budgets;
    private readonly string run;
    private readonly TimeSpan timeout;
    public BoundedRunController(DurableGateway gateway, DurableBudgetStore budgets, string run, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentException("Invalid tool timeout.");
        }

        if (budgets.GetLimits(run).Deadline > gateway.GetGrant(run)!.ExpiresAt)
        {
            throw new ArgumentException("Runtime deadline cannot extend the grant.");
        }

        this.gateway = gateway;
        this.budgets = budgets;
        this.run = run;
        this.timeout = timeout;
    }

    public async Task<Decision> RunAsync(ExecutionQuote quote, Func<CancellationToken, Task<Decision>> action, CancellationToken cancellation = default)
    {
        string? reason = budgets.Reserve(run, quote, out string? reservation);
        if (reason is not null)
        {
            gateway.RecordRuntimeDenial(run, reason);
            return new(Outcome.Denied, reason);
        }

        using var signal = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        signal.CancelAfter(timeout);
        bool interrupted = false;
        string? inactive = null;
        async Task Watch()
        {
            try
            {
                while (!signal.IsCancellationRequested)
                {
                    inactive = budgets.CheckActive(run);
                    if (inactive is not null)
                    {
                        signal.Cancel();
                        break;
                    }

                    await Task.Delay(50, signal.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e) when (e is AuthorizationDependencyException or IOException or UnauthorizedAccessException)
            {
                inactive = "runtime_revoked";
                signal.Cancel();
            }
        }

        Task watch = Watch();
        // A synchronous or noncooperative delegate cannot prevent the controller timing out.
        Task<Decision> work = Task.Run(() =>
        {
            signal.Token.ThrowIfCancellationRequested();
            return action(signal.Token);
        });
        try
        {
            return await work.WaitAsync(signal.Token);
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
            reason = inactive ?? (cancellation.IsCancellationRequested ? "runtime_cancelled" : "tool_timeout");
            gateway.SignalRevoke(run);
            budgets.Revoke(run, reason);
            // Work may already have committed or may ignore cancellation. Require effect inspection.
            return new(Outcome.RecoveryRequired, "execution_interrupted_inspect_effects");
        }
        finally
        {
            signal.Cancel();
            await watch;
            if (!interrupted || work.IsCompleted)
            {
                budgets.Release(run, reservation!);
            }
            else
            {
                _ = work.ContinueWith(t =>
                {
                    _ = t.Exception;
                }, TaskScheduler.Default); // Observe faults; retain quarantined reservation.
            }
        }
    }

    public void Revoke()
    {
        gateway.SignalRevoke(run);
        budgets.Revoke(run);
    }

    public void Stop()
    {
        gateway.SignalStop();
        budgets.Stop();
    }
}
