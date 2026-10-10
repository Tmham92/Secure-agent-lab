using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class AuditMutationReorderRemovalAndSnapshotIsolationCase
{
    internal static void Run()
    {
        var g = new Gateway(new FakeClock());
        string run = Session(g);
        g.Execute(run, Read());
        IReadOnlyList<AuditEntry> old = g.GetAuditSnapshot();
        g.Execute(run, Publish());
        CheckAssertions.Equal(1, old.Count);
        AuditEntry[] entries = g.GetAuditSnapshot().ToArray();
        CheckAssertions.Assert(AuditChain.VerifyAudit(entries), "Original chain invalid");
        AuditEntry e = entries[0];
        AuditEntry[] mutations = [e with
        {
            Reason = "tampered"
        }, e with
        {
            Sequence = 2
        }, e with
        {
            UtcTime = e.UtcTime.AddSeconds(1)
        }, e with
        {
            RunId = "forged"
        }, e with
        {
            Proposal = Read("secrets")
        }, e with
        {
            Outcome = Outcome.Denied
        }, e with
        {
            PreviousHash = "fake"
        }, e with
        {
            Hash = "fake"
        }

        ];
        foreach (AuditEntry mutation in mutations)
        {
            CheckAssertions.Assert(!AuditChain.VerifyAudit(new[] { mutation, entries[1] }), "Mutation undetected");
        }
        CheckAssertions.
                Assert(!AuditChain.VerifyAudit(entries.Reverse()), "Reorder undetected");
        CheckAssertions.Assert(!AuditChain.VerifyAudit(entries.Skip(1)), "Missing head undetected");
        CheckAssertions.Assert(AuditChain.VerifyAudit(entries.Take(1)), "Valid prefix should pass; no trusted checkpoint");
        CheckAssertions.Assert(AuditChain.VerifyAudit([]), "Empty prefix should pass; no trusted checkpoint");
        // Changing a copied array cannot alter the gateway-owned records.
        entries[0] = mutations[0];
        CheckAssertions.Assert(AuditChain.VerifyAudit(g.GetAuditSnapshot()), "Snapshot leaked mutable state");
        CheckAssertions.Assert(old is IList<AuditEntry> list && list.IsReadOnly, "Snapshot collection is writable");
    }
}
