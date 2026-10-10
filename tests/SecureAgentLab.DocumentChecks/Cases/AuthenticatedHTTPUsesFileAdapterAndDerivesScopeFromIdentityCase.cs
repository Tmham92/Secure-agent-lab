using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.DocumentChecks.Fixtures;
using SecureAgentLab.Transport.Configuration;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.Transport.Requests;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class AuthenticatedHTTPUsesFileAdapterAndDerivesScopeFromIdentityCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, GatewayForCallback GatewayFor, GrantCallback Grant)
    {
        string root = Fixture();
        Gateway gateway = GatewayFor(root);
        var settings = new CredentialSettings("document-host", "document-gateway", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using WebApplication app = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"], settings, gateway: gateway, allowLoopbackHttp: true);
        await app.StartAsync();
        try
        {
            string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient
            {
                BaseAddress = new(address)
            };
            using HttpResponseMessage unauth = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(read));
            CheckAssertions.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);
            string run = Grant(gateway, "documents/task");
            client.DefaultRequestHeaders.Authorization = new("Bearer", new CredentialService(settings).Issue(run, "worker", TimeSpan.FromMinutes(2), gateway.GetGrant(run)!.Fingerprint));
            using HttpResponseMessage allowed = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(read));
            CheckAssertions.Equal(Gateway.TaskDocument, (await allowed.Content.ReadFromJsonAsync<Decision>())!.Result);
            using HttpResponseMessage other = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(new(Operation.ReadDocument, "documents/reference")));
            CheckAssertions.Equal(Outcome.Denied, (await other.Content.ReadFromJsonAsync<Decision>())!.Outcome);
            CheckAssertions.Equal(new MockEffects(1, 0), gateway.GetEffects());
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
