using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ApprovalCannotCrossSessionsOrAlteredProposalsCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g);
        string other = Session(g);
        string ticket = Approve(g, run);
        Expect(g.Execute(other, Publish(), ticket), Outcome.Denied, "approval_mismatch");
        Expect(g.Execute(run, Publish(estimate: 1), ticket), Outcome.Denied, "approval_mismatch");
        Expect(g.Execute(run, Publish("reports/other"), ticket), Outcome.Denied, "out_of_scope");
        Expect(g.Execute(run, Read(), ticket), Outcome.Allowed, "scoped_read");
        Effects(g, 1, 0);
        // Invalid redemption attempts did not consume the rightful approval.
        Expect(g.Execute(run, Publish(), ticket), Outcome.Allowed, "publication_approved");
        Effects(g, 1, 1);
        CheckAssertions.Throws<InvalidOperationException>(() => g.ApprovePublication(run, Publish("reports/other"), TimeSpan.FromMinutes(1)));
        CheckAssertions.Throws<InvalidOperationException>(() => g.ApprovePublication(run, Read(), TimeSpan.FromMinutes(1)));
    }
}
