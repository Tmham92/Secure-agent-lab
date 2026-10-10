using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Comparisons.Vulnerable;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Portable.PortableScenarioSupport;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class RetryScenario
{
    internal static Comparison Run(string root) => Pair("4a-retry", "Durable idempotency across lost acknowledgement and restart", "Commit reviewed draft; lose reply; retry same key after reopening state", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "retry");
        string ledger = Path.Combine(f.Unsafe.Directory, "publications.txt");
        try
        {
            File.AppendAllLines(ledger, [ComparisonFixture.Reviewed]);
            throw new LostAcknowledgement();
        }
        catch (LostAcknowledgement)
        {
        } // Commit succeeds; caller really observes an injected lost acknowledgement.

        File.AppendAllLines(ledger, [ComparisonFixture.Reviewed]); // Reopened executor has no idempotency record.
        using var secure = new DurableFixture(f.Secure.Directory);
        DurableGateway g = secure.Open();
        string run = DurableFixture.Run(g);
        string ticket = g.ApprovePublication(run, Draft(), TimeSpan.FromMinutes(1));
        secure.Sink.LoseAck = true;
        Decision uncertain = g.Execute(run, Draft(), ticket, "once");
        Require(uncertain.Outcome == Outcome.RecoveryRequired, "lost acknowledgment was not observed");
        secure.Sink.LoseAck = false;
        g = secure.Open();
        Decision retry = g.Execute(run, Draft(), ticket, "once");
        Require(retry.Reason == "publication_replayed" && g.GetEffects().Publications == 1 && File.ReadAllLines(ledger).Length == 2, "retry effects not 2 versus 1");
        bool positive = Positive(g, run);
        g.Stop();
        return (Observe("unsafe", "lost acknowledgement then duplicate retry", true, ("publications", File.ReadAllLines(ledger).Length), ("restart", true)), Observe("secure", uncertain.Reason + " -> " + retry.Reason, positive, ("publications", 1), ("restart", true)));
    });
}
