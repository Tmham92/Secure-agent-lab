using System.Net;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class OperatorCannotIssueUnknownOrExpandedGrantsCase
{
    internal static async Task RunAsync(string operatorToken, global::SecureAgentLab.Core.Contracts.Proposal publish, ExecuteCallback Execute, IssueCallback Issue, StatusCallback Status)
    {
        await Status(operatorToken, "/operator/runs", new IssueRunRequest(Permissions: [new(Operation.ExternalRequest, "outside")]), HttpStatusCode.BadRequest);
        await Status(operatorToken, "/operator/runs", new IssueRunRequest(LifetimeSeconds: 301), HttpStatusCode.BadRequest);
        IssuedRun run = await Issue(new IssueRunRequest(Permissions: [new(Operation.ReadDocument, "documents/task")]));
        CheckAssertions.Equal(Outcome.Denied, (await Execute(run, publish)).Outcome);
        await Status(operatorToken, $"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(publish), HttpStatusCode.Conflict);
    }
}
