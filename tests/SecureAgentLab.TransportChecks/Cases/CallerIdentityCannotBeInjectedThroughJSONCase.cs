using System.Net;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class CallerIdentityCannotBeInjectedThroughJSONCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Execution.Gateway gateway, global::SecureAgentLab.Core.Contracts.Proposal read, ExecuteCallback Execute, IssueCallback Issue, StatusCallback Status)
    {
        IssuedRun run = await Issue();
        MockEffects before = gateway.GetEffects();
        await Status(run.WorkerCredential, "/worker/proposals", new
        {
            proposal = read,
            runId = "forged"
        }, HttpStatusCode.BadRequest);
        CheckAssertions.Equal(before, gateway.GetEffects());
        CheckAssertions.Equal(Outcome.Allowed, (await Execute(run, read)).Outcome);
        CheckAssertions.Equal(run.RunId, gateway.GetAuditSnapshot()[^1].RunId);
    }
}
