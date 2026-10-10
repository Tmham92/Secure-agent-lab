using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Recovery;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class TailTruncationIsDetectedUsingIndependentSignedHeadCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        g.Execute(run, read);
        string path = Path.Combine(f.Log, "audit.jsonl");
        string[] lines = File.ReadAllLines(path);
        File.WriteAllText(path, string.Join('\n', lines.Take(lines.Length - 1)) + "\n");
        CheckAssertions.Throws<AuthorizationDependencyException>(() => f.Open());
    }
}
