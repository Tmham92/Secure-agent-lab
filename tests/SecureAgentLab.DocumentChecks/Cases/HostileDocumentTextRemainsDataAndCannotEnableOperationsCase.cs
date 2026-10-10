using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class HostileDocumentTextRemainsDataAndCannotEnableOperationsCase
{
    internal static void Run(FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        Gateway g = GatewayFor(Fixture());
        string run = Grant(g, "documents/reference");
        CheckAssertions.Equal(SyntheticDocuments.Reference, g.Execute(run, new(Operation.ReadDocument, "documents/reference")).Result);
        foreach (Proposal? p in new[]
        {
            new Proposal(Operation.ReadDocument, "documents/private"),
            new(Operation.ExternalRequest, "https://example.invalid"),
            new(Operation.ChangePermissions, "grants/admin"),
            new(Operation.PublishReport, "reports/draft")
        }

        )
        {
            CheckAssertions.Equal(Outcome.Denied, g.Execute(run, p).Outcome);
        }
        CheckAssertions.
                Equal(new MockEffects(1, 0), g.GetEffects());
    }
}
