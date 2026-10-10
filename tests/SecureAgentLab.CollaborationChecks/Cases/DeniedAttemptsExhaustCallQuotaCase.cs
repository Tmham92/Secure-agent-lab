using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class DeniedAttemptsExhaustCallQuotaCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture(2);
        Assert(!f.Send("researcher", "writer", "unknown", "x").Allowed);
        Assert(!f.Send("researcher", "writer", "unknown", "x").Allowed);
        Assert(f.Send("researcher", "writer", "facts", "20 + 22").Reason == "call_budget");
        Assert(f.Broker.Delivered == 0);
    }
}
