using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ExpiryAtExactDeadlineAndRevocationCase
{
    internal static void Run()
    {
        var clock = new FakeClock();
        var g = new Gateway(clock);
        string run = Session(g);
        clock.Advance(TimeSpan.FromMinutes(5));
        Expect(g.Execute(run, Read()), Outcome.Denied, "identity_expired");
        string other = Session(g);
        string ticket = Approve(g, other);
        g.Revoke(other);
        Expect(g.Execute(other, Read()), Outcome.Denied, "identity_revoked");
        Expect(g.Execute(other, Publish(), ticket), Outcome.Denied, "identity_revoked");
        CheckAssertions.Throws<InvalidOperationException>(() => Approve(g, other));
        Effects(g, 0, 0);
    }
}
