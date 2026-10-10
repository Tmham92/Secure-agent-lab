using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class InvalidHostConfigurationRejectedZeroQuotasValidCase
{
    internal static void Run()
    {
        var g = new Gateway();
        CheckAssertions.Throws<ArgumentOutOfRangeException>(() => g.CreateSession(TimeSpan.Zero, 1, 1));
        CheckAssertions.Throws<ArgumentOutOfRangeException>(() => Session(g, calls: -1));
        CheckAssertions.Throws<ArgumentOutOfRangeException>(() => Session(g, bytes: -1));
        string run = Session(g, calls: 0, bytes: 0);
        Expect(g.Execute(run, Read()), Outcome.Denied, "call_budget_exhausted");
        CheckAssertions.Throws<ArgumentOutOfRangeException>(() => g.ApprovePublication(run, Publish(), TimeSpan.Zero));
    }
}
