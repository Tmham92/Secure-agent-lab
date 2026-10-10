using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Collaboration;
using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class CollaborationAPIsRequireHTTPSUnlessLoopbackHTTPIsExplicitlyEnabledCase
{
    internal static async Task RunAsync(AssertCallback Assert)
    {
        using var f = new Fixture();
        await using WebApplication app = CollaborationApi.Build(["--urls", "http://127.0.0.1:0"], f.Credentials, broker: f.Broker);
        await app.StartAsync();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/worker/send")
        {
            Content = JsonContent.Create(new SendMessage("writer", "facts", "20 + 22"))
        };
        request.Headers.Authorization = new("Bearer", f.Tokens["researcher"]);
        using HttpResponseMessage response = await http.SendAsync(request);
        Assert(response.StatusCode == HttpStatusCode.BadRequest && f.Broker.Delivered == 0);
        await app.StopAsync();
    }
}
