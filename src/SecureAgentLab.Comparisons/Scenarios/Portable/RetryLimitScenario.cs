using System.Security.Cryptography;
using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Comparisons.Vulnerable;
using SecureAgentLab.Durable.Budgets;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class RetryLimitScenario
{
    internal static Comparison Run(string root) => Pair("9b-retry-limit", "Accounted bounded retry policy", "Fixed failing fixture retried 5 times; scenario allows initial attempt plus 1 retry", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "retry-limit");
        var unsafeTool = new FailingFixture();
        var secureTool = new FailingFixture();
        for (int i = 0; i < 5; i++)
        {
            try
            {
                unsafeTool.Execute();
            }
            catch (FixedToolFailure)
            {
            }
        }

        var store = new DurableBudgetStore(Path.Combine(f.Secure.Directory, "budget"), RandomNumberGenerator.GetBytes(32));
        store.Create("retry", new(DateTimeOffset.UtcNow.AddMinutes(3), 10, 20, 20, 20, Retries: 1));
        for (int ordinal = 0; ordinal < 5; ordinal++)
        {
            if (store.Reserve("retry", new(1, 1, 1, ordinal), out string? reservation) is null)
            {
                try
                {
                    secureTool.Execute();
                }
                catch (FixedToolFailure)
                {
                }
                finally
                {
                    store.Release("retry", reservation!);
                }
            }
        }

        Require(unsafeTool.Attempts == 5 && secureTool.Attempts == 2, "retry fixture effects not bounded");
        return (Observe("unsafe", "retry policy absent, fixed outer maximum retained", unsafeTool.Attempts > 0, ("fixtureAttempts", unsafeTool.Attempts)), Observe("secure", "excess retries denied before fixture", secureTool.Attempts > 0, ("fixtureAttempts", secureTool.Attempts)));
    });
}
