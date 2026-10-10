using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class PublicationRequiresHostApprovalForgedAndReplayedTicketsDeniedCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g);
        Expect(g.Execute(run, Publish()), Outcome.ApprovalRequired, "publication_approval_required");
        Expect(g.Execute(run, Publish(), "forged"), Outcome.Denied, "approval_unknown");
        Effects(g, 0, 0);
        string ticket = Approve(g, run);
        Expect(g.Execute(run, Publish(), ticket), Outcome.Allowed, "publication_approved");
        Expect(g.Execute(run, Publish(), ticket), Outcome.Denied, "approval_consumed");
        Effects(g, 0, 1);
    }
}
