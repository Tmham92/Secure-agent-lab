using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class SharedCacheArtifactsAndWritableLogsExposeNoAlternateChannelCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        foreach (string? action in new[]
        {
            "cache",
            "artifacts",
            "logs",
            "permissions"
        }

        )
        {
            Assert(!f.Broker.Execute(f.Caller("researcher"), action).Allowed);
        }
        Assert(!f.Receive("writer", "researcher", "facts").Allowed && f.Broker.Delivered == 0);
    }
}
