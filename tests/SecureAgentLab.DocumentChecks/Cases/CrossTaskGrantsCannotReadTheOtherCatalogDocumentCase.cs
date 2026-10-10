using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class CrossTaskGrantsCannotReadTheOtherCatalogDocumentCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        Gateway g = GatewayFor(Fixture());
        string a = Grant(g, "documents/task");
        string b = Grant(g, "documents/reference");
        CheckAssertions.Equal(Outcome.Denied, g.Execute(a, new(Operation.ReadDocument, "documents/reference")).Outcome);
        CheckAssertions.Equal(Outcome.Denied, g.Execute(b, read).Outcome);
        CheckAssertions.Equal(SyntheticDocuments.Reference, g.Execute(b, new(Operation.ReadDocument, "documents/reference")).Result);
        CheckAssertions.Equal(new MockEffects(1, 0), g.GetEffects());
    }
}
