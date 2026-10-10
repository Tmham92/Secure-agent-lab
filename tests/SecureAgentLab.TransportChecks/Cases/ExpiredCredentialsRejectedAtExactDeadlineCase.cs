using System.Net;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class ExpiredCredentialsRejectedAtExactDeadlineCase
{
    internal static async Task RunAsync(global::SecureAgentLab.TransportChecks.Fixtures.TestClock clock, global::SecureAgentLab.Core.Execution.Gateway gateway, global::SecureAgentLab.Core.Contracts.Proposal read, IssueCallback Issue, StatusCallback Status)
    {
        IssuedRun run = await Issue(new IssueRunRequest(LifetimeSeconds: 1));
        MockEffects before = gateway.GetEffects();
        clock.Advance(TimeSpan.FromSeconds(1));
        await Status(run.WorkerCredential, "/worker/proposals", new ExecuteRequest(read), HttpStatusCode.Unauthorized);
        CheckAssertions.Equal(before, gateway.GetEffects());
    }
}
