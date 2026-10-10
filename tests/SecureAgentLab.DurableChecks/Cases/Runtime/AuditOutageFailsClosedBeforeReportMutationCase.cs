using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class AuditOutageFailsClosedBeforeReportMutationCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        string run = f.Run(g);
        string ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        f.Sink.Offline = true;
        Phase7Checks.Equal(Outcome.Denied, g.Execute(run, draft, ticket, "a").Outcome);
        g.SignalRevoke(run);
        f.Sink.Offline = false;
        Phase7Checks.Equal(new ReportSnapshot(0, null), g.GetReport());
        Phase7Checks.Equal("identity_revoked", g.Execute(run, draft, ticket, "a").Reason);
    }
}
