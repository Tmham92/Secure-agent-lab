using System.Net;
using System.Text;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class MissingForgedMalformedAndOversizedCredentialsRejectedBeforeExecutionCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Execution.Gateway gateway, global::SecureAgentLab.Transport.Credentials.CredentialService issuer, global::SecureAgentLab.Core.Contracts.Proposal read, StatusCallback Status)
    {
        MockEffects before = gateway.GetEffects();
        int audit = gateway.GetAuditSnapshot().Count;
        foreach (string? token in new string?[]
        {
            null,
            "forged",
            "v1.bad.bad",
            new string ('x', 9000)
        }

        )
        {
            await Status(token, "/worker/proposals", new ExecuteRequest(read), HttpStatusCode.Unauthorized);
        }

        string valid = issuer.Issue("unknown", "worker", TimeSpan.FromMinutes(1), "grant");
        string[] pieces = valid.Split('.');
        string altered = "v1." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")) + "." + pieces[2];
        await Status(altered, "/worker/proposals", new ExecuteRequest(read), HttpStatusCode.Unauthorized);
        CheckAssertions.Equal(before, gateway.GetEffects());
        CheckAssertions.Equal(audit, gateway.GetAuditSnapshot().Count);
    }
}
