using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.TransportChecks.Fixtures;
namespace SecureAgentLab.TransportChecks.Cases;

internal static class HTTPSIsRequiredUnlessTheLoopbackLabExceptionIsExplicitCase
{
    internal static async Task RunAsync(global::SecureAgentLab.TransportChecks.Fixtures.TestClock clock, string operatorToken, global::SecureAgentLab.Transport.Configuration.CredentialSettings settings)
    {
        var strictGateway = new Gateway(clock);
        await using WebApplication strict = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Lab:AllowLoopbackHttp", "false", "--Logging:LogLevel:Default", "Warning"], settings, clock, strictGateway);
        await strict.StartAsync();
        string strictAddress = strict.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(strictAddress)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        using HttpResponseMessage response = await client.PostAsJsonAsync("/operator/runs", new IssueRunRequest());
        CheckAssertions.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        CheckAssertions.Equal(new MockEffects(0, 0), strictGateway.GetEffects());
        await strict.StopAsync();
    }
}
