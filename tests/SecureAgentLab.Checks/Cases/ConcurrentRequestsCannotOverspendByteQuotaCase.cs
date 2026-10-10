using System.Text;
using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ConcurrentRequestsCannotOverspendByteQuotaCase
{
    internal static void Run()
    {
        var g = new Gateway();
        int bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
        string run = Session(g, calls: 100, bytes: bytes * 3L);
        var results = new Decision[100];
        Parallel.For(0, results.Length, i => results[i] = g.Execute(run, Read(estimate: 0)));
        CheckAssertions.Equal(3, results.Count(d => d.Outcome == Outcome.Allowed));
        CheckAssertions.Equal(97, results.Count(d => d.Reason == "response_budget_exhausted"));
        CheckAssertions.Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        Effects(g, 3, 0);
    }
}
