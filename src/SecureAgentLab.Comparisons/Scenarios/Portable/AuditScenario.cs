using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Portable.PortableScenarioSupport;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class AuditScenario
{
    internal static Comparison Run(string root) => Pair("4b-audit", "Independent signed audit checkpoint", "Delete final committed event and rewrite/re-hash earlier history", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "audit");
        using var secure = new DurableFixture(f.Secure.Directory);
        DurableGateway g = secure.Open();
        string run = DurableFixture.Run(g);
        string ticket = g.ApprovePublication(run, Draft(), TimeSpan.FromMinutes(1));
        Require(g.Execute(run, Draft(), ticket, "audit").Outcome == Outcome.Allowed, "initial publication");
        AuditEntry[] original = g.GetAuditSnapshot().ToArray();
        AuditEntry[] prefix = original[..^1];
        AuditEntry[] rewritten = prefix.Select(e => e with { Reason = "concealed synthetic history", PreviousHash = "", Hash = "" }).ToArray();
        for (int i = 0; i < rewritten.Length; i++)
        {
            rewritten[i] = rewritten[i] with
            {
                PreviousHash = i == 0 ? "" : rewritten[i - 1].Hash
            };
            rewritten[i] = rewritten[i] with
            {
                Hash = AuditChain.ComputeHash(rewritten[i])
            };
        }

        Require(AuditChain.VerifyAudit(prefix) && AuditChain.VerifyAudit(rewritten), "local rewrite did not conceal history");
        Require(secure.Verify(original) && !secure.Verify(prefix) && !secure.Verify(rewritten), "independent head missed mutation/truncation");
        File.WriteAllText(Path.Combine(f.Unsafe.Directory, "rewritten-audit.json"), System.Text.Json.JsonSerializer.Serialize(rewritten));
        Proposal another = Draft(ComparisonFixture.Replaced) with
        {
            ExpectedVersion = 1
        };
        string second = g.ApprovePublication(run, another, TimeSpan.FromMinutes(1));
        int before = g.GetEffects().Publications;
        secure.Sink.Offline = true;
        Decision outage = g.Execute(run, another, second, "outage");
        secure.Sink.Offline = false;
        Require(outage.Outcome != Outcome.Allowed && g.GetEffects().Publications == before, "audit outage created an effect");
        bool positive = Positive(g, run);
        g.Stop();
        return (Observe("unsafe", "rewritten/truncated local chain accepted", true, ("concealedPublication", true), ("localValidation", true)), Observe("secure", "independent head rejects both histories; outage blocks write", positive, ("tamperingDetected", true), ("outageNewPublications", 0), ("earlierPublicationsPreserved", 1)));
    });
}
