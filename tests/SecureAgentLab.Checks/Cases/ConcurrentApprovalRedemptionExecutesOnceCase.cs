using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ConcurrentApprovalRedemptionExecutesOnceCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g, calls: 100);
        string ticket = Approve(g, run);
        var results = new Decision[100];
        Parallel.For(0, results.Length, i => results[i] = g.Execute(run, Publish(), ticket));
        CheckAssertions.Equal(1, results.Count(d => d.Outcome == Outcome.Allowed));
        CheckAssertions.Equal(99, results.Count(d => d.Reason == "approval_consumed"));
        Effects(g, 0, 1);
    }
}
