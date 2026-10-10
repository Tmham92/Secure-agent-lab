using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Persistence;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Durable.Budgets;
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
        if (key.Length < 32)
        {
            throw new ArgumentException("Runtime integrity key is too short.");
        }

        this.directory = Path.GetFullPath(directory);
        this.key = key.ToArray();
        this.clock = clock ?? TimeProvider.System;
        path = Path.Combine(this.directory, "budgets.json");
    }

    private string Seal(BudgetState state) => Convert.ToHexString(HMACSHA256.HashData(key, JsonSerializer.SerializeToUtf8Bytes(state)));
    private BudgetState Load(bool create = false)
    {
        try
        {
            return LoadState(create);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            throw new AuthorizationDependencyException("runtime_state_unavailable", e);
        }
    }

    private BudgetState LoadState(bool create)
    {
        if (!File.Exists(path))
        {
            if (!create)
            {
                throw new AuthorizationDependencyException("runtime_state_missing");
            }

            return new()
            {
                Updated = clock.GetUtcNow()
            };
        }

        SealedBudget saved = DurableFiles.Read<SealedBudget>(path);
        if (saved.State is null || saved.AuthenticationCode is null || saved.State.Runs is null)
        {
            throw new AuthorizationDependencyException("runtime_state_tampered");
        }

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(saved.AuthenticationCode), Convert.FromHexString(Seal(saved.State))))
            {
                throw new AuthorizationDependencyException("runtime_state_tampered");
            }
        }
        catch (FormatException)
        {
            throw new AuthorizationDependencyException("runtime_state_tampered");
        }

        if (saved.State.Updated > clock.GetUtcNow())
        {
            throw new AuthorizationDependencyException("runtime_clock_regressed");
        }

        return saved.State;
    }

    private void Save(BudgetState state)
    {
        state.Updated = clock.GetUtcNow();
        DurableFiles.Write(path, new SealedBudget(state, Seal(state)));
    }

    public void Create(string run, RunLimits limits)
    {
        limits.Validate();
        using IDisposable lease = DurableFiles.Lock(directory);
        BudgetState state = Load(create: true);
        if (state.Stopped || limits.Deadline <= clock.GetUtcNow() || state.Runs.Count >= 1000)
        {
            throw new InvalidOperationException("Runtime cannot issue this run.");
        }

        if (!state.Runs.TryAdd(DurableFiles.Hash(run), new()
        {
            Limits = limits,
            Attempts = limits.Attempts,
            InputTokens = limits.InputTokens,
            OutputTokens = limits.OutputTokens,
            CostMicros = limits.CostMicros
        }))
        {
            throw new InvalidOperationException("Runtime run already exists; budgets cannot be reset.");
        }

        Save(state);
    }

    public string? Reserve(string run, ExecutionQuote quote, out string? reservation)
    {
        if (quote.InputTokens < 0 || quote.OutputTokens < 0 || quote.CostMicros < 0 || quote.RetryOrdinal < 0)
        {
            throw new ArgumentException("Invalid trusted quote.");
        }

        reservation = null;
        using IDisposable lease = DurableFiles.Lock(directory);
        BudgetState state = Load();
        if (!state.Runs.TryGetValue(DurableFiles.Hash(run), out BudgetRun? budget))
        {
            return "runtime_revoked";
        }

        string? reason = Inactive(state, budget);
        if (reason is not null)
        {
            return reason;
        }

        if (budget.Attempts == 0)
        {
            return "runtime_call_budget";
        }

        budget.Attempts--; // All active admission attempts, including quota/fanout denials.
        reason = quote.RetryOrdinal > budget.Limits.Retries ? "runtime_retry_budget" : budget.Active.Count >= budget.Limits.Concurrency ? "runtime_fanout" : quote.InputTokens > budget.InputTokens || quote.OutputTokens > budget.OutputTokens ? "runtime_token_budget" : quote.CostMicros > budget.CostMicros ? "runtime_cost_budget" : null;
        if (reason is null)
        {
            reservation = Guid.NewGuid().ToString("N");
            budget.Active.Add(reservation);
            budget.InputTokens -= quote.InputTokens;
            budget.OutputTokens -= quote.OutputTokens;
            budget.CostMicros -= quote.CostMicros;
        }

        budget.LastDenial = reason;
        Save(state);
        return reason;
    }

    private string? Inactive(BudgetState state, BudgetRun run) => state.Stopped ? "runtime_stopped" : run.Revoked ? "runtime_revoked" : clock.GetUtcNow() >= run.Limits.Deadline ? "runtime_deadline" : null;
    public string? CheckActive(string run)
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        BudgetState state = Load();
        return state.Runs.TryGetValue(DurableFiles.Hash(run), out BudgetRun? value) ? Inactive(state, value) : "runtime_revoked";
    }

    public RunLimits GetLimits(string run)
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        return Load().Runs[DurableFiles.Hash(run)].Limits;
    }

    public BudgetSnapshot Snapshot(string run)
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        BudgetState state = Load();
        BudgetRun value = state.Runs[DurableFiles.Hash(run)];
        return new(value.Attempts, value.InputTokens, value.OutputTokens, value.CostMicros, value.Active.Count, value.Revoked, state.Stopped, value.LastDenial);
    }

    public void Release(string run, string reservation)
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        BudgetState state = Load();
        state.Runs[DurableFiles.Hash(run)].Active.Remove(reservation);
        Save(state); // Release concurrency, never charged budgets.
    }

    public void Revoke(string run, string reason = "runtime_revoked")
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        BudgetState state = Load();
        if (state.Runs.TryGetValue(DurableFiles.Hash(run), out BudgetRun? budget))
        {
            budget.Revoked = true;
            budget.LastDenial = reason;
        }

        Save(state);
    }

    public void Stop()
    {
        using IDisposable lease = DurableFiles.Lock(directory);
        BudgetState state = Load();
        state.Stopped = true;
        Save(state);
    }
}
