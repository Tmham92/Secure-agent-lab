using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Comparisons.Vulnerable;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Portable.PortableScenarioSupport;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class ActionsScenario
{
    internal static Comparison Run(string root) => Pair("2-actions", "Immutable operation grants", "ExternalRequest, MessageAgent, ChangePermissions to synthetic-local-store", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "actions");
        var vulnerable = new DeliberatelyVulnerable();
        Gateway g = f.Secure.ReaderGateway();
        string run = ComparisonFixture.Run(g);
        foreach (Operation op in new[]
        {
            Operation.ExternalRequest,
            Operation.MessageAgent,
            Operation.ChangePermissions
        }

        )
        {
            var proposal = new Proposal(op, "synthetic-local-store");
            vulnerable.Execute(proposal);
            Require(g.Execute(run, proposal).Outcome == Outcome.Denied, "out-of-scope operation executed");
        }

        Require(vulnerable.Capture.SequenceEqual([ComparisonFixture.FakeSecret]) && vulnerable.Messages == 1 && vulnerable.Admin, "unsafe actions did not occur");
        Require(g.GetEffects() == new MockEffects(0, 0), "secure out-of-scope side effects");
        return (Observe("unsafe", "executor skipped grants", vulnerable.ReadTask("task-a", "task-a") == Gateway.TaskDocument, ("captureRecords", vulnerable.Capture.Count), ("messages", vulnerable.Messages), ("permissionAdmin", vulnerable.Admin)), Observe("secure", "all three proposals denied", Positive(g, run), ("captureRecords", 0), ("messages", 0), ("permissionAdmin", false)));
    });
}
