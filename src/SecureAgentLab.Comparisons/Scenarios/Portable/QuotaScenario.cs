using System.Security.Cryptography;
using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Models;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class QuotaScenario
{
    internal static Comparison Run(string root) => Pair("9a-quota", "Atomic shared reservations", "Two coordinated requests each cost 1 against shared allowance 1", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "quota");
        int remaining = 1;
        int effects = 0;
        using var barrier = new Barrier(2);
        void UnsafeRequest()
        {
            bool allowed = Volatile.Read(ref remaining) >= 1;
            Require(barrier.SignalAndWait(TimeSpan.FromSeconds(5)), "coordination failed");
            if (allowed)
            {
                Interlocked.Decrement(ref remaining);
                Interlocked.Increment(ref effects);
            }
        }

        Parallel.Invoke(UnsafeRequest, UnsafeRequest);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string path = Path.Combine(f.Secure.Directory, "budget");
        var store = new DurableBudgetStore(path, key);
        store.Create("shared", new(DateTimeOffset.UtcNow.AddMinutes(3), 5, 1, 1, 1, 2));
        int admitted = 0;
        Parallel.For(0, 2, index =>
        {
            var replica = new DurableBudgetStore(path, key);
            if (replica.Reserve("shared", new(1, 1, 1), out _) is null)
            {
                Interlocked.Increment(ref admitted);
            }
        });
        BudgetSnapshot snapshot = store.Snapshot("shared");
        Require(effects == 2 && remaining == -1 && admitted == 1 && snapshot.CostMicros == 0, "atomic overspend comparison");
        return (Observe("unsafe", "both checked before either charged", effects > 0, ("admitted", effects), ("remaining", remaining)), Observe("secure", "one reserved, other denied", admitted > 0, ("admitted", admitted), ("remaining", snapshot.CostMicros)));
    });
}
