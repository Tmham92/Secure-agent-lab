using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class StopBlocksExecutionAndFurtherHostIssuanceCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g);
        string ticket = Approve(g, run);
        g.Stop();
        g.Stop();
        Expect(g.Execute(run, Publish(), ticket), Outcome.Denied, "gateway_stopped");
        Expect(g.Execute(run, Read()), Outcome.Denied, "gateway_stopped");
        CheckAssertions.Throws<InvalidOperationException>(() => Session(g));
        CheckAssertions.Throws<InvalidOperationException>(() => Approve(g, run));
        Effects(g, 0, 0);
    }
}
