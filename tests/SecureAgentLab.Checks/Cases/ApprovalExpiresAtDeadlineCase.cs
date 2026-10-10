using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ApprovalExpiresAtDeadlineCase
{
    internal static void Run()
    {
        var clock = new FakeClock();
        var g = new Gateway(clock);
        string run = Session(g);
        string ticket = Approve(g, run);
        clock.Advance(TimeSpan.FromMinutes(1));
        Expect(g.Execute(run, Publish(), ticket), Outcome.Denied, "approval_expired");
        Effects(g, 0, 0);
    }
}
