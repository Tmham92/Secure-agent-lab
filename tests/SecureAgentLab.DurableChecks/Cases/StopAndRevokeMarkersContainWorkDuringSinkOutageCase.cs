using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Recovery;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class StopAndRevokeMarkersContainWorkDuringSinkOutageCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        f.Sink.Offline = true;
        CheckAssertions.Throws<AuthorizationDependencyException>(() => g.Revoke(run));
        CheckAssertions.Equal("identity_revoked", g.Execute(run, read).Reason);
        CheckAssertions.Throws<AuthorizationDependencyException>(() => g.Stop());
        CheckAssertions.Equal("gateway_stopped", g.Execute(run, read).Reason);
        f.Sink.Offline = false;
        CheckAssertions.Equal("gateway_stopped", f.Open().Execute(run, read).Reason);
        IReadOnlyList<AuditEntry> events = g.GetAuditSnapshot();
        CheckAssertions.Assert(events.Any(e => e.EventType == "run_revoked") && events.Any(e => e.EventType == "gateway_stopped"), "Outage containment was not reconciled to audit");
        CheckAssertions.Equal(new MockEffects(0, 0), g.GetEffects());
    }
}
