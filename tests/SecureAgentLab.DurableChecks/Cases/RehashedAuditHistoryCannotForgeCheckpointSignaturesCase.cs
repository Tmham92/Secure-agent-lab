using System.Text.Json;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Recovery;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class RehashedAuditHistoryCannotForgeCheckpointSignaturesCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        g.Execute(run, read);
        string path = Path.Combine(f.Log, "audit.jsonl");
        var events = f.Sink.GetEvents().ToArray();
        var entry = events[^1].Entry with
        {
            Reason = "rewritten"
        };
        entry = entry with
        {
            Hash = AuditChain.ComputeHash(entry)
        };
        events[^1] = new(entry, events[^1].Checkpoint with
        {
            Value = events[^1].Checkpoint.Value with
            {
                Hash = entry.Hash
            }
        });
        File.WriteAllText(path, string.Join('\n', events.Select(e => JsonSerializer.Serialize(e))) + "\n");
        CheckAssertions.Throws<AuthorizationDependencyException>(() => f.Open());
    }
}
