using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class OversizedDocumentsNeverReturnPartialContentOrChargeResponseBytesCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        string root = Fixture();
        File.WriteAllText(Path.Combine(root, "task", "task.txt"), new string('X', 70000));
        Gateway g = GatewayFor(root);
        string run = Grant(g, "documents/task", 65536);
        Decision result = g.Execute(run, read);
        CheckAssertions.Equal("document_too_large", result.Reason);
        CheckAssertions.Equal<string?>(null, result.Result);
        CheckAssertions.Equal(65536L, g.GetSession(run)!.RemainingResponseBytes);
        CheckAssertions.Equal(0, g.GetEffects().DocumentReads);
    }
}
