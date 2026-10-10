using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class ConcurrentSendsAtomicallyEnforceQuotaAndOneConsumerMailboxCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture(3);
        var sends = new BrokerResult[50];
        Parallel.For(0, sends.Length, i => sends[i] = f.Send("researcher", "writer", "facts", "x"));
        Assert(sends.Count(r => r.Allowed) == 3 && f.Broker.Delivered == 3);
        var receives = new BrokerResult[50];
        Parallel.For(0, receives.Length, i => receives[i] = f.Receive("writer", "researcher", "facts"));
        Assert(receives.Count(r => r.Allowed) == 3);
        Assert(receives.Where(r => r.Allowed).Select(r => r.Message!.Id).Distinct().Count() == 3);
    }
}
