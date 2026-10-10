using System.Net;
using System.Net.Http.Json;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class HTTPApprovalBindingOneTimePublicationAndRevokeCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Execution.Gateway gateway, string operatorToken, global::SecureAgentLab.Core.Contracts.Proposal publish, global::SecureAgentLab.Core.Contracts.Proposal read, ExecuteCallback Execute, IssueCallback Issue, SendCallback Send, StatusCallback Status)
    {
        IssuedRun run = await Issue();
        MockEffects before = gateway.GetEffects();
        CheckAssertions.Equal(Outcome.ApprovalRequired, (await Execute(run, publish)).Outcome);
        using HttpResponseMessage approvalResponse = await Send(operatorToken, HttpMethod.Post, $"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(publish));
        approvalResponse.EnsureSuccessStatusCode();
        string ticket = (await approvalResponse.Content.ReadFromJsonAsync<IssuedApproval>())!.Ticket;
        IssuedRun other = await Issue();
        CheckAssertions.Equal("approval_mismatch", (await Execute(other, publish, ticket)).Reason);
        CheckAssertions.Equal("publication_approved", (await Execute(run, publish, ticket)).Reason);
        CheckAssertions.Equal("approval_consumed", (await Execute(run, publish, ticket)).Reason);
        CheckAssertions.Equal(before.Publications + 1, gateway.GetEffects().Publications);
        await Status(operatorToken, $"/operator/runs/{run.RunId}/revoke", new
        {
        }, HttpStatusCode.NoContent);
        CheckAssertions.Equal("identity_revoked", (await Execute(run, read)).Reason);
    }
}
