using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Comparisons.Vulnerable;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Portable.PortableScenarioSupport;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class ApprovalScenario
{
    internal static Comparison Run(string root) => Pair("3-approval", "Exact content approval binding", "Approve reviewed draft, then submit substituted draft", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "approval");
        var unsafeExecutor = new DeliberatelyVulnerable();
        unsafeExecutor.ApprovePublishOperation();
        unsafeExecutor.PublishWithOperationOnlyApproval(ComparisonFixture.Replaced);
        using var secure = new DurableFixture(f.Secure.Directory);
        DurableGateway g = secure.Open();
        string run = DurableFixture.Run(g);
        string ticket = g.ApprovePublication(run, Draft(), TimeSpan.FromMinutes(1));
        Decision rejected = g.Execute(run, Draft(ComparisonFixture.Replaced), ticket, "replace");
        Require(rejected.Outcome == Outcome.Denied && g.GetEffects().Publications == 0, "changed draft was published");
        Require(g.Execute(run, Draft(), ticket, "original").Outcome == Outcome.Allowed && g.GetReport() == new ReportSnapshot(1, ComparisonFixture.Reviewed), "reviewed draft positive control");
        Require(unsafeExecutor.Publications.SequenceEqual([ComparisonFixture.Replaced]), "unsafe substitution did not happen");
        bool positive = Positive(g, run);
        g.Stop();
        return (Observe("unsafe", "operation-only approval accepted substituted text", true, ("publishedText", unsafeExecutor.Publications.Single())), Observe("secure", rejected.Reason, positive, ("substitutedPublications", 0), ("originalPublications", 1), ("publishedText", ComparisonFixture.Reviewed)));
    });
}
