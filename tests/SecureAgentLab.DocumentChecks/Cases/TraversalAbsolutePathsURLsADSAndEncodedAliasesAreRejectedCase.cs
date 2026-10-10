using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class TraversalAbsolutePathsURLsADSAndEncodedAliasesAreRejectedCase
{
    internal static void Run(FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant, ReaderCallback Reader)
    {
        string root = Fixture();
        Gateway g = GatewayFor(root);
        string run = Grant(g, "documents/task");
        foreach (string? id in new[]
        {
            "../task/task.txt",
            "documents/../private",
            "/etc/passwd",
            "C:\\secret.txt",
            "documents/task:secret",
            "documents%2ftask",
            "documents/task/",
            "DOCUMENTS/task",
            "http://127.0.0.1/task"
        }

        )
        {
            CheckAssertions.Equal(Outcome.Denied, g.Execute(run, new(Operation.ReadDocument, id)).Outcome);
            CheckAssertions.Equal(Outcome.Denied, Reader(root).Read(id, 4096).Outcome);
        }
        CheckAssertions.
                Equal(new MockEffects(0, 0), g.GetEffects());
    }
}
