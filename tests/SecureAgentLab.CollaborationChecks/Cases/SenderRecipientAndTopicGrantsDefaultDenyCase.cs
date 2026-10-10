using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class SenderRecipientAndTopicGrantsDefaultDenyCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        Assert(!f.Send("researcher", "writer", "supervisor", "approval").Allowed);
        Assert(!f.Send("researcher", "researcher", "facts", "cache").Allowed);
        Assert(!f.Send("writer", "researcher", "facts", "wrong-direction").Allowed);
        Assert(f.Broker.Delivered == 0);
    }
}
