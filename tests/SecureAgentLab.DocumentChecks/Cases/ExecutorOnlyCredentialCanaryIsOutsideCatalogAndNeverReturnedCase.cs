using System.Text.Json;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class ExecutorOnlyCredentialCanaryIsOutsideCatalogAndNeverReturnedCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        string root = Fixture();
        string secret = "synthetic-executor-only-credential";
        File.WriteAllText(Path.Combine(root, "credential.txt"), secret);
        Gateway g = GatewayFor(root);
        string run = Grant(g, "documents/task");
        foreach (string? id in new[]
        {
            "documents/private",
            "../credential.txt",
            Path.Combine(root, "credential.txt")
        }

        )
        {
            CheckAssertions.Equal<string?>(null, g.Execute(run, new(Operation.ReadDocument, id)).Result);
        }
        CheckAssertions.
                Assert(!g.Execute(run, read).Result!.Contains(secret), "Credential canary leaked");
        CheckAssertions.Assert(!JsonSerializer.Serialize(g.GetAuditSnapshot()).Contains(secret), "Credential canary leaked into audit");
    }
}
