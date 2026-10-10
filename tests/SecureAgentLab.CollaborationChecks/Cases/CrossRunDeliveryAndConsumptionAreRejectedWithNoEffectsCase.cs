using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class CrossRunDeliveryAndConsumptionAreRejectedWithNoEffectsCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        f.Register("other", "other-run", [new("researcher", "other", "facts"), new("other", "writer", "facts")]);
        Assert(!f.Send("other", "writer", "facts", "cross-run").Allowed);
        Assert(!f.Receive("other", "researcher", "facts").Allowed);
        Assert(!f.Send("researcher", "other", "facts", "cross-run").Allowed);
        Assert(f.Broker.Delivered == 0);
    }
}
