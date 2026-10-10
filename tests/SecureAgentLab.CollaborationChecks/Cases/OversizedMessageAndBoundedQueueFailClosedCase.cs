using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class OversizedMessageAndBoundedQueueFailClosedCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture(bytes: 8192);
        Assert(!f.Send("researcher", "writer", "facts", new string('x', 513)).Allowed);
        for (int i = 0; i < 8; i++)
        {
            Assert(f.Send("researcher", "writer", "facts", "x").Allowed);
        }
        Assert(f.Send("researcher", "writer", "facts", "x").Reason == "mailbox_capacity");
        Assert(f.Broker.Delivered == 8);
    }
}
