using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class MeasuredUTF8AndReceiveByteBudgetsRejectBeforeMailboxEffectsCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture(bytes: 6);
        Assert(!f.Send("researcher", "writer", "facts", "€€€").Allowed);
        Assert(f.Send("researcher", "writer", "facts", "€€").Allowed);
        Assert(f.Receive("writer", "researcher", "facts").Allowed);
        Assert(!f.Send("researcher", "writer", "facts", "a").Allowed);
        Assert(!f.Submit("42").Allowed); // Writer charged measured receive bytes, not only sends.
        Assert(f.Broker.Delivered == 1 && f.Broker.Submissions == 0);
    }
}
