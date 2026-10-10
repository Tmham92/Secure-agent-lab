using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Comparisons.Vulnerable;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Portable.PortableScenarioSupport;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class VersionScenario
{
    internal static Comparison Run(string root) => Pair("8-version", "Expected resource version", "Two exact-content approvals against version 0, publish first then stale second", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "version");
        var unsafeLedger = new MissingVersionStore();
        unsafeLedger.Approve(ComparisonFixture.Reviewed, 0);
        unsafeLedger.Approve(ComparisonFixture.Replaced, 0);
        unsafeLedger.Write(ComparisonFixture.Reviewed, 0);
        unsafeLedger.Write(ComparisonFixture.Replaced, 0);
        File.WriteAllText(Path.Combine(f.Unsafe.Directory, "report.txt"), unsafeLedger.Content); // Exact contents approved separately; only CAS omitted.
        Require(unsafeLedger.Content == ComparisonFixture.Replaced && unsafeLedger.Version == 2, "unsafe stale overwrite absent");
        using var secure = new DurableFixture(f.Secure.Directory);
        DurableGateway g = secure.Open();
        string run = DurableFixture.Run(g);
        Proposal first = Draft();
        Proposal second = Draft(ComparisonFixture.Replaced);
        string a = g.ApprovePublication(run, first, TimeSpan.FromMinutes(1));
        string b = g.ApprovePublication(run, second, TimeSpan.FromMinutes(1));
        Require(g.Execute(run, first, a, "first").Outcome == Outcome.Allowed, "first approved write");
        Decision stale = g.Execute(run, second, b, "second");
        Require(stale.Reason == "resource_version_conflict" && g.GetReport() == new ReportSnapshot(1, ComparisonFixture.Reviewed), "stale version overwrite");
        bool positive = Positive(g, run);
        g.Stop();
        return (Observe("unsafe", "last writer overwrote earlier approved content", true, ("content", File.ReadAllText(Path.Combine(f.Unsafe.Directory, "report.txt"))), ("version", 2)), Observe("secure", stale.Reason, positive, ("content", ComparisonFixture.Reviewed), ("version", 1)));
    });
}
