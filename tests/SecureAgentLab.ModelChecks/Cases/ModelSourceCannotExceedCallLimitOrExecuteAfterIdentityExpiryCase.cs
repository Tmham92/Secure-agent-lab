using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class ModelSourceCannotExceedCallLimitOrExecuteAfterIdentityExpiryCase
{
    internal static void Run(string Valid, BytesCallback Bytes)
    {
        Proposal p = new ModelProposalSource(Bytes(Valid)).GetProposals().Single();
        var clock = new ModelClock();
        var g = new Gateway(clock);
        string run = g.CreateSession(TimeSpan.FromMinutes(1), 1, 4096);
        CheckAssertions.Equal(Outcome.Allowed, g.Execute(run, p).Outcome);
        CheckAssertions.Equal("call_budget_exhausted", g.Execute(run, p).Reason);
        string expired = g.CreateSession(TimeSpan.FromMinutes(1), 20, 4096);
        clock.Now = clock.Now.AddMinutes(1);
        CheckAssertions.Equal("identity_expired", g.Execute(expired, p).Reason);
        CheckAssertions.Equal(new MockEffects(1, 0), g.GetEffects());
    }
}
