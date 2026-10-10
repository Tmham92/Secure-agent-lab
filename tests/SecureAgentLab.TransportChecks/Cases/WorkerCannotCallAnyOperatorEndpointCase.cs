using System.Net;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class WorkerCannotCallAnyOperatorEndpointCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Execution.Gateway gateway, string operatorToken, global::SecureAgentLab.Core.Contracts.Proposal publish, global::SecureAgentLab.Core.Contracts.Proposal read, ExecuteCallback Execute, IssueCallback Issue, SendCallback Send, StatusCallback Status)
    {
        IssuedRun run = await Issue();
        MockEffects before = gateway.GetEffects();
        await Status(run.WorkerCredential, "/operator/runs", new IssueRunRequest(), HttpStatusCode.Forbidden);
        await Status(run.WorkerCredential, $"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(publish), HttpStatusCode.Forbidden);
        await Status(run.WorkerCredential, $"/operator/runs/{run.RunId}/revoke", new
        {
        }, HttpStatusCode.Forbidden);
        await Status(run.WorkerCredential, "/operator/stop", new
        {
        }, HttpStatusCode.Forbidden);
        foreach (string? path in new[]
        {
            "/operator/audit",
            "/operator/effects"
        }

        )
        {
            using HttpResponseMessage response = await Send(run.WorkerCredential, HttpMethod.Get, path);
            CheckAssertions.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        CheckAssertions.
                Equal(before, gateway.GetEffects());
        CheckAssertions.Equal(Outcome.Allowed, (await Execute(run, read)).Outcome); // Neither stop nor revoke happened.
        using HttpResponseMessage operatorWorkerCall = await Send(operatorToken, HttpMethod.Post, "/worker/proposals", new ExecuteRequest(read));
        CheckAssertions.Equal(HttpStatusCode.Forbidden, operatorWorkerCall.StatusCode);
    }
}
