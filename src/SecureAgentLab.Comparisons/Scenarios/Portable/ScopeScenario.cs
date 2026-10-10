using System.Text;
using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Comparisons.Vulnerable;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Policy;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Portable.PortableScenarioSupport;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class ScopeScenario
{
    internal static Comparison Run(string root) => Pair("1-scope", "Authenticated task binding", "Authenticated task A asks for task B", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "scope");
        var vulnerable = new DeliberatelyVulnerable();
        foreach (ComparisonFixture? fixture in new[]
        {
            f.Unsafe,
            f.Secure
        }

        )
        {
            File.WriteAllText(Path.Combine(fixture.Documents, "reference", "reference.txt"), ComparisonFixture.FakeSecret);
        }

        string leaked = vulnerable.ReadTask("task-a", "task-b");
        IEnumerable<DocumentEntry> catalog = SyntheticDocuments.Catalog.Select(e => e.Resource == "documents/reference" ? e with { Sha256 = SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(ComparisonFixture.FakeSecret)) } : e);
        var g = new Gateway(new DefaultPolicy(), new DocumentExecutor(new FileDocumentReader(f.Secure.Documents, catalog)));
        string run = g.CreateSession(new("synthetic-v1", DateTimeOffset.UtcNow.AddMinutes(3), 10, 8192, [new(Operation.ReadDocument, "documents/task")]));
        Decision denied = g.Execute(run, new(Operation.ReadDocument, "documents/reference"));
        Require(leaked == ComparisonFixture.FakeSecret && denied.Outcome == Outcome.Denied && denied.Result is null && g.GetEffects().DocumentReads == 0, "cross-task leakage comparison");
        return (Observe("unsafe", "caller task field trusted", vulnerable.ReadTask("task-a", "task-a") == Gateway.TaskDocument, ("foreignTaskBytes", leaked)), Observe("secure", denied.Reason, Positive(g, run), ("foreignTaskBytes", ""), ("deniedReadEffects", 0)));
    });
}
