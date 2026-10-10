using System.Net;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class SignedUnknownRunAndMismatchedGrantRejectedBeforeExecutionCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Execution.Gateway gateway, global::SecureAgentLab.Transport.Credentials.CredentialService issuer, global::SecureAgentLab.Core.Contracts.Proposal read, IssueCallback Issue, StatusCallback Status)
    {
        IssuedRun run = await Issue();
        MockEffects before = gateway.GetEffects();
        int audit = gateway.GetAuditSnapshot().Count;
        foreach (string? token in new[]
        {
            issuer.Issue("unknown", "worker", TimeSpan.FromMinutes(1), run.GrantFingerprint),
            issuer.Issue(run.RunId, "worker", TimeSpan.FromMinutes(1), "changed-grant")
        }

        )
        {
            await Status(token, "/worker/proposals", new ExecuteRequest(read), HttpStatusCode.Forbidden);
        }
        CheckAssertions.
                Equal(before, gateway.GetEffects());
        CheckAssertions.Equal(audit, gateway.GetAuditSnapshot().Count);
    }
}
