using System.Net;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Configuration;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class WrongIssuerWrongAudienceAndFutureCredentialsRejectedCase
{
    internal static async Task RunAsync(global::SecureAgentLab.TransportChecks.Fixtures.TestClock clock, global::SecureAgentLab.Core.Execution.Gateway gateway, global::SecureAgentLab.Core.Contracts.Proposal read, global::SecureAgentLab.Transport.Configuration.CredentialSettings settings, StatusCallback Status)
    {
        MockEffects before = gateway.GetEffects();
        foreach (CredentialSettings? wrong in new[]
        {
            settings with
            {
                Issuer = "other"
            },
            settings with
            {
                Audience = "other"
            }
        }

        )
        {
            string token = new CredentialService(wrong, clock).Issue("unknown", "worker", TimeSpan.FromMinutes(1), "grant");
            await Status(token, "/worker/proposals", new ExecuteRequest(read), HttpStatusCode.Unauthorized);
        }

        var future = new TestClock(clock.GetUtcNow().AddMinutes(1));
        await Status(new CredentialService(settings, future).Issue("unknown", "worker", TimeSpan.FromMinutes(1), "grant"), "/worker/proposals", new ExecuteRequest(read), HttpStatusCode.Unauthorized);
        CheckAssertions.Equal(before, gateway.GetEffects());
    }
}
