using System.Net.Http.Json;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class ExpiredGrantDeniedEvenWithLaterValidCredentialCase
{
    internal static async Task RunAsync(global::SecureAgentLab.TransportChecks.Fixtures.TestClock clock, global::SecureAgentLab.Core.Execution.Gateway gateway, global::SecureAgentLab.Transport.Credentials.CredentialService issuer, global::SecureAgentLab.Core.Contracts.Proposal read, IssueCallback Issue, SendCallback Send)
    {
        IssuedRun run = await Issue(new IssueRunRequest(LifetimeSeconds: 1));
        string token = issuer.Issue(run.RunId, "worker", TimeSpan.FromMinutes(1), run.GrantFingerprint);
        clock.Advance(TimeSpan.FromSeconds(1));
        MockEffects before = gateway.GetEffects();
        using HttpResponseMessage response = await Send(token, HttpMethod.Post, "/worker/proposals", new ExecuteRequest(read));
        response.EnsureSuccessStatusCode();
        CheckAssertions.Equal("identity_expired", (await response.Content.ReadFromJsonAsync<Decision>())!.Reason);
        CheckAssertions.Equal(before, gateway.GetEffects());
    }
}
