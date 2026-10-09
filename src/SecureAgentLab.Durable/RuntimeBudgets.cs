using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Core;

namespace SecureAgentLab.Durable;

public sealed record RunLimits(DateTimeOffset Deadline, int Attempts, long InputTokens, long OutputTokens,
    long CostMicros, int Concurrency = 1, int Retries = 0)
{
    public void Validate()
    {
        if (Deadline.Offset != TimeSpan.Zero || Attempts is < 1 or > 1000 || InputTokens < 0 || OutputTokens < 0 ||
            CostMicros < 0 || Concurrency is < 1 or > 4 || Retries is < 0 or > 10) throw new ArgumentException("Invalid runtime limits.");
    }
}
public sealed record ExecutionQuote(long InputTokens, long OutputTokens, long CostMicros, int RetryOrdinal = 0);
public sealed record BudgetSnapshot(int Attempts, long InputTokens, long OutputTokens, long CostMicros,
    int Active, bool Revoked, bool Stopped, string? LastDenial);
public sealed class BudgetRun
{
    public required RunLimits Limits { get; init; }
    public int Attempts { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CostMicros { get; set; }
    public HashSet<string> Active { get; init; } = [];
    public bool Revoked { get; set; }
    public string? LastDenial { get; set; }
}
public sealed class BudgetState
{
    public Dictionary<string, BudgetRun> Runs { get; init; } = new(StringComparer.Ordinal);
    public bool Stopped { get; set; }
    public DateTimeOffset Updated { get; set; }
}
public sealed record SealedBudget(BudgetState State, string AuthenticationCode);

// Conservative upper-bound reservations. Never refund on failure, interruption or process crash.
// Multiple trusted coordinators share this local filesystem lock, not a distributed database.
public sealed class DurableBudgetStore
{
    private readonly string directory;
    private readonly string path;
    private readonly byte[] key;
    private readonly TimeProvider clock;
    public DurableBudgetStore(string directory, byte[] key, TimeProvider? clock = null)
    {
        if (key.Length < 32) throw new ArgumentException("Runtime integrity key is too short.");
        this.directory = Path.GetFullPath(directory); this.key = key.ToArray(); this.clock = clock ?? TimeProvider.System;
        path = Path.Combine(this.directory,"budgets.json");
    }
    private string Seal(BudgetState state) => Convert.ToHexString(HMACSHA256.HashData(key,JsonSerializer.SerializeToUtf8Bytes(state)));
    private BudgetState Load(bool create = false)
    {
        try { return LoadState(create); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException)
        { throw new AuthorizationDependencyException("runtime_state_unavailable", e); }
    }
    private BudgetState LoadState(bool create)
    {
        if (!File.Exists(path))
        {
            if (!create) throw new AuthorizationDependencyException("runtime_state_missing");
            return new() { Updated = clock.GetUtcNow() };
        }
        var saved = DurableFiles.Read<SealedBudget>(path);
        if (saved.State is null || saved.AuthenticationCode is null || saved.State.Runs is null)
            throw new AuthorizationDependencyException("runtime_state_tampered");
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(saved.AuthenticationCode),Convert.FromHexString(Seal(saved.State))))
                throw new AuthorizationDependencyException("runtime_state_tampered");
        }
        catch (FormatException) { throw new AuthorizationDependencyException("runtime_state_tampered"); }
        if (saved.State.Updated > clock.GetUtcNow()) throw new AuthorizationDependencyException("runtime_clock_regressed");
        return saved.State;
    }
    private void Save(BudgetState state) { state.Updated = clock.GetUtcNow(); DurableFiles.Write(path,new SealedBudget(state,Seal(state))); }
    public void Create(string run, RunLimits limits)
    {
        limits.Validate();
        using var lease = DurableFiles.Lock(directory); var state = Load(create:true);
        if (state.Stopped || limits.Deadline <= clock.GetUtcNow() || state.Runs.Count >= 1000) throw new InvalidOperationException("Runtime cannot issue this run.");
        if (!state.Runs.TryAdd(DurableFiles.Hash(run),new() { Limits = limits, Attempts = limits.Attempts,
            InputTokens = limits.InputTokens, OutputTokens = limits.OutputTokens, CostMicros = limits.CostMicros }))
            throw new InvalidOperationException("Runtime run already exists; budgets cannot be reset.");
        Save(state);
    }
    public string? Reserve(string run, ExecutionQuote quote, out string? reservation)
    {
        if (quote.InputTokens < 0 || quote.OutputTokens < 0 || quote.CostMicros < 0 || quote.RetryOrdinal < 0)
            throw new ArgumentException("Invalid trusted quote.");
        reservation = null;
        using var lease = DurableFiles.Lock(directory); var state = Load();
        if (!state.Runs.TryGetValue(DurableFiles.Hash(run),out var budget)) return "runtime_revoked";
        var reason = Inactive(state,budget);
        if (reason is not null) return reason;
        if (budget.Attempts == 0) return "runtime_call_budget";
        budget.Attempts--; // All active admission attempts, including quota/fanout denials.
        reason = quote.RetryOrdinal > budget.Limits.Retries ? "runtime_retry_budget" :
            budget.Active.Count >= budget.Limits.Concurrency ? "runtime_fanout" :
            quote.InputTokens > budget.InputTokens || quote.OutputTokens > budget.OutputTokens ? "runtime_token_budget" :
            quote.CostMicros > budget.CostMicros ? "runtime_cost_budget" : null;
        if (reason is null)
        {
            reservation = Guid.NewGuid().ToString("N"); budget.Active.Add(reservation);
            budget.InputTokens -= quote.InputTokens; budget.OutputTokens -= quote.OutputTokens; budget.CostMicros -= quote.CostMicros;
        }
        budget.LastDenial = reason; Save(state); return reason;
    }
    private string? Inactive(BudgetState state, BudgetRun run) => state.Stopped ? "runtime_stopped" : run.Revoked ? "runtime_revoked" :
        clock.GetUtcNow() >= run.Limits.Deadline ? "runtime_deadline" : null;
    public string? CheckActive(string run)
    {
        using var lease = DurableFiles.Lock(directory); var state = Load();
        return state.Runs.TryGetValue(DurableFiles.Hash(run),out var value) ? Inactive(state,value) : "runtime_revoked";
    }
    public RunLimits GetLimits(string run)
    { using var lease = DurableFiles.Lock(directory); return Load().Runs[DurableFiles.Hash(run)].Limits; }
    public BudgetSnapshot Snapshot(string run)
    {
        using var lease = DurableFiles.Lock(directory); var state = Load(); var value = state.Runs[DurableFiles.Hash(run)];
        return new(value.Attempts,value.InputTokens,value.OutputTokens,value.CostMicros,value.Active.Count,value.Revoked,state.Stopped,value.LastDenial);
    }
    public void Release(string run,string reservation)
    {
        using var lease = DurableFiles.Lock(directory); var state = Load();
        state.Runs[DurableFiles.Hash(run)].Active.Remove(reservation); Save(state); // Release concurrency, never charged budgets.
    }
    public void Revoke(string run,string reason = "runtime_revoked")
    {
        using var lease = DurableFiles.Lock(directory); var state = Load();
        if (state.Runs.TryGetValue(DurableFiles.Hash(run),out var budget)) { budget.Revoked = true; budget.LastDenial = reason; }
        Save(state);
    }
    public void Stop()
    { using var lease = DurableFiles.Lock(directory); var state = Load(); state.Stopped = true; Save(state); }
}

public sealed class BoundedRunController
{
    private readonly DurableGateway gateway;
    private readonly DurableBudgetStore budgets;
    private readonly string run;
    private readonly TimeSpan timeout;
    public BoundedRunController(DurableGateway gateway,DurableBudgetStore budgets,string run,TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1)) throw new ArgumentException("Invalid tool timeout.");
        if (budgets.GetLimits(run).Deadline > gateway.GetGrant(run)!.ExpiresAt) throw new ArgumentException("Runtime deadline cannot extend the grant.");
        this.gateway = gateway; this.budgets = budgets; this.run = run; this.timeout = timeout;
    }
    public async Task<Decision> RunAsync(ExecutionQuote quote,Func<CancellationToken,Task<Decision>> action,CancellationToken cancellation = default)
    {
        var reason = budgets.Reserve(run,quote,out var reservation);
        if (reason is not null) { gateway.RecordRuntimeDenial(run,reason); return new(Outcome.Denied,reason); }
        using var signal = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        signal.CancelAfter(timeout);
        var interrupted = false;
        string? inactive = null;
        async Task Watch()
        {
            try
            {
                while (!signal.IsCancellationRequested)
                {
                    inactive = budgets.CheckActive(run);
                    if (inactive is not null) { signal.Cancel(); break; }
                    await Task.Delay(50,signal.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is AuthorizationDependencyException or IOException or UnauthorizedAccessException)
            { inactive = "runtime_revoked"; signal.Cancel(); }
        }
        var watch = Watch();
        // A synchronous or noncooperative delegate cannot prevent the controller timing out.
        var work = Task.Run(() => { signal.Token.ThrowIfCancellationRequested(); return action(signal.Token); });
        try { return await work.WaitAsync(signal.Token); }
        catch (OperationCanceledException)
        {
            interrupted = true;
            reason = inactive ?? (cancellation.IsCancellationRequested ? "runtime_cancelled" : "tool_timeout");
            gateway.SignalRevoke(run); budgets.Revoke(run,reason);
            // Work may already have committed or may ignore cancellation. Require effect inspection.
            return new(Outcome.RecoveryRequired,"execution_interrupted_inspect_effects");
        }
        finally
        {
            signal.Cancel(); await watch;
            if (!interrupted || work.IsCompleted) budgets.Release(run,reservation!);
            else _ = work.ContinueWith(t => { _ = t.Exception; },TaskScheduler.Default); // Observe faults; retain quarantined reservation.
        }
    }
    public void Revoke() { gateway.SignalRevoke(run); budgets.Revoke(run); }
    public void Stop() { gateway.SignalStop(); budgets.Stop(); }
}
