using System.Net;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class OperatorStopBlocksLaterWorkerExecutionAndNewGrantsCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Execution.Gateway gateway, string operatorToken, global::SecureAgentLab.Core.Contracts.Proposal read, ExecuteCallback Execute, IssueCallback Issue, StatusCallback Status)
    {
        IssuedRun run = await Issue();
        MockEffects before = gateway.GetEffects();
        await Status(operatorToken, "/operator/stop", new
        {
        }, HttpStatusCode.NoContent);
        CheckAssertions.Equal("gateway_stopped", (await Execute(run, read)).Reason);
        await Status(operatorToken, "/operator/runs", new IssueRunRequest(), HttpStatusCode.Conflict);
        CheckAssertions.Equal(before, gateway.GetEffects());
        CheckAssertions.Assert(AuditChain.VerifyAudit(gateway.GetAuditSnapshot()), "Transport audit invalid");
    }
}
