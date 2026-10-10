using System.Text.Json;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class MissingExecutorFileDeniesWithoutPathsSecretBytesOrReadEffectsCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        string root = Fixture();
        File.Delete(Path.Combine(root, "task", "task.txt"));
        Gateway g = GatewayFor(root);
        string run = Grant(g, "documents/task");
        Decision result = g.Execute(run, read);
        CheckAssertions.Equal(new Decision(Outcome.Denied, "document_unavailable"), result);
        CheckAssertions.Equal(19, g.GetSession(run)!.RemainingCalls);
        CheckAssertions.Equal(0, g.GetEffects().DocumentReads);
        CheckAssertions.Assert(!JsonSerializer.Serialize(g.GetAuditSnapshot()).Contains(root), "Host path leaked");
    }
}
