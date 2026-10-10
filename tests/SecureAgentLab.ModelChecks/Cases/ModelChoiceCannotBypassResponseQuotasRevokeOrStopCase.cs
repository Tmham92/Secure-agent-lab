using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class ModelChoiceCannotBypassResponseQuotasRevokeOrStopCase
{
    internal static void Run(string Valid, BytesCallback Bytes)
    {
        Proposal p = new ModelProposalSource(Bytes(Valid)).GetProposals().Single();
        var g = new Gateway();
        string run = g.CreateSession(TimeSpan.FromMinutes(1), 20, 0);
        CheckAssertions.Equal(Outcome.Denied, g.Execute(run, p).Outcome);
        g.Revoke(run);
        CheckAssertions.Equal("identity_revoked", g.Execute(run, p).Reason);
        g.Stop();
        CheckAssertions.Equal("gateway_stopped", g.Execute(run, p).Reason);
        CheckAssertions.Equal(new MockEffects(0, 0), g.GetEffects());
    }
}
