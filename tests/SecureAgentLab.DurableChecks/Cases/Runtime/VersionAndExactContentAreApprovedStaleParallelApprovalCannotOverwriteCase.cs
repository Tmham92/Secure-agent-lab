using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class VersionAndExactContentAreApprovedStaleParallelApprovalCannotOverwriteCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        Phase7Checks.Equal(new ReportSnapshot(0, null), g.GetReport());
        Phase7Checks.Equal(Outcome.ApprovalRequired, g.Execute(run, draft).Outcome);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        string second = g.ApprovePublication(run, draft with
        {
            Content = "Other reviewed report."
        }, TimeSpan.FromMinutes(1));
        Phase7Checks.Equal(0L, g.PreviewPublication(run, draft).ExpectedVersion);
        Phase7Checks.Equal("approval_mismatch", g.Execute(run, draft with
        {
            Content = "Injected replacement"
        }, ticket, "a").Reason);
        Phase7Checks.Equal("approval_mismatch", g.Execute(run, draft with
        {
            ExpectedVersion = 1
        }, ticket, "a").Reason);
        Phase7Checks.Equal("publication_approved", g.Execute(run, draft, ticket, "a").Reason);
        Phase7Checks.Equal("resource_version_conflict", Phase7Checks.Open(f).Execute(run, draft with
        {
            Content = "Other reviewed report."
        }, second, "b").Reason);
        Phase7Checks.Equal("publication_replayed", Phase7Checks.Open(f).Execute(run, draft, ticket, "a").Reason);
        Phase7Checks.Equal(new ReportSnapshot(1, draft.Content), Phase7Checks.Open(f).GetReport());
        Phase7Checks.Equal(new MockEffects(0, 1), g.GetEffects());
        g.Stop();
        Phase7Checks.Equal(new ReportSnapshot(1, draft.Content), g.GetReport());
    }
}
