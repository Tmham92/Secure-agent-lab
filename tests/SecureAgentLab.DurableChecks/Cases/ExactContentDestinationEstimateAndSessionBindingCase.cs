using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class ExactContentDestinationEstimateAndSessionBindingCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        string other = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        PublicationPreview preview = g.PreviewPublication(run, draft);
        CheckAssertions.Equal(draft.Content!, preview.Content);
        CheckAssertions.Equal(DurableGateway.ContentHash(draft), preview.ContentHash);
        CheckAssertions.Equal("approval_mismatch", g.Execute(run, draft with
        {
            Content = "Changed content"
        }, ticket, "key").Reason);
        CheckAssertions.Equal("out_of_scope", g.Execute(run, draft with
        {
            Resource = "reports/other"
        }, ticket, "key").Reason);
        CheckAssertions.Equal("approval_mismatch", g.Execute(run, draft with
        {
            EstimatedBytes = 1
        }, ticket, "key").Reason);
        CheckAssertions.Equal("approval_mismatch", g.Execute(other, draft, ticket, "key").Reason);
        CheckAssertions.Equal("approval_unknown", g.Execute(run, draft, "forged", "key").Reason);
        CheckAssertions.Equal("idempotency_key_required", g.Execute(run, draft, ticket, null).Reason);
        CheckAssertions.Equal(new MockEffects(0, 0), g.GetEffects());
        CheckAssertions.Equal("publication_approved", g.Execute(run, draft, ticket, "key").Reason);
    }
}
