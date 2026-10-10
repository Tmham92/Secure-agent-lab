using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class UnknownIdentityCannotExecuteCase
{
    internal static void Run()
    {
        var g = new Gateway();
        Expect(g.Execute("forged", Read()), Outcome.Denied, "unknown_identity");
        Expect(g.Execute(null, Read()), Outcome.Denied, "unknown_identity");
        Effects(g, 0, 0);
        CheckAssertions.Equal(2, g.GetAuditSnapshot().Count);
    }
}
