using System.Text.Json;
using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class AuditCapacityExhaustionStopsEffectsWithoutDroppingEarlierEvidenceCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        for (int i = 0; i < 4096; i++)
        {
            f.Broker.Execute(f.Caller("researcher"), "cache");
        }
        Assert(f.Send("writer", "researcher", "ack", "x").Reason == "audit_capacity");
        Assert(f.Broker.Delivered == 0);
        var e = f.Broker.Seal("demo-run", new string('A', 64));
        Assert(JsonSerializer.Deserialize<Transcript>(Convert.FromBase64String(e.Payload))!.Events.Length == 4096);
    }
}
