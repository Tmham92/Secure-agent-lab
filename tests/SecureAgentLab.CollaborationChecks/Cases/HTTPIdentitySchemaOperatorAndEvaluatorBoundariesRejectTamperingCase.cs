using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Collaboration;
using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.Collaboration.Evaluation;
using SecureAgentLab.CollaborationChecks.Fixtures;
using SecureAgentLab.Transport.Credentials;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class HTTPIdentitySchemaOperatorAndEvaluatorBoundariesRejectTamperingCase
{
    internal static async Task RunAsync(AssertCallback Assert)
    {
        using var f = new Fixture();
        await using WebApplication app = CollaborationApi.Build(["--urls", "http://127.0.0.1:0"], f.Credentials, broker: f.Broker, allowLoopbackHttp: true);
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient
        {
            BaseAddress = new Uri(address),
            Timeout = TimeSpan.FromSeconds(10)
        };
        async Task Status(string? token, string path, string json, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (token is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using HttpResponseMessage response = await http.SendAsync(request);
            Assert(response.StatusCode == expected, $"{path}: {response.StatusCode} expected {expected}");
        }

        string body = "{\"recipient\":\"writer\",\"topic\":\"facts\",\"text\":\"20 + 22\"}";
        foreach (string? token in new string?[]
        {
            null,
            "forged",
            f.Credentials.Issue("unknown", "worker", TimeSpan.FromMinutes(1), "unknown")
        }

        )
        {
            if (token?.StartsWith("v1.") == true)
            { // Valid signature, unknown host grant: no delivery.
                using var request = new HttpRequestMessage(HttpMethod.Post, "/worker/send")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new("Bearer", token);
                using HttpResponseMessage response = await http.SendAsync(request);
                Assert(!(await response.Content.ReadFromJsonAsync<BrokerResult>())!.Allowed);
            }
            else
            {
                await Status(token, "/worker/send", body, HttpStatusCode.Unauthorized);
            }
        }

        await Status(f.Tokens["researcher"], "/worker/send", body[..^1] + ",\"sender\":\"supervisor\",\"role\":\"operator\"}", HttpStatusCode.BadRequest);
        await Status(f.Tokens["researcher"], "/operator/seal", "{\"run\":\"demo-run\",\"challenge\":\"x\"}", HttpStatusCode.Forbidden);
        await Status(f.Tokens["researcher"], "/operator/agents", "{}", HttpStatusCode.Forbidden);
        await Status(f.Tokens["researcher"], "/worker/send", "{\"recipient\":\"writer\",\"topic\":\"facts\",\"text\":null}", HttpStatusCode.OK);
        await Status(f.Credentials.Issue("host", "operator", TimeSpan.FromMinutes(1)), "/worker/send", body, HttpStatusCode.Forbidden);
        var wrongAudience = new CredentialService(new("phase8-supervisor", "different", f.Key), f.Clock);
        await Status(wrongAudience.Issue("researcher", "worker", TimeSpan.FromMinutes(1), f.Caller("researcher").GrantFingerprint), "/worker/send", body, HttpStatusCode.Unauthorized);
        foreach (string? channel in new[]
        {
            "cache",
            "artifacts",
            "logs"
        }

        )
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/worker/" + channel);
            request.Headers.Authorization = new("Bearer", f.Tokens["researcher"]);
            using HttpResponseMessage response = await http.SendAsync(request);
            Assert(!(await response.Content.ReadFromJsonAsync<BrokerResult>())!.Allowed);
        }
        Assert(f.Broker.Delivered == 0 && f.Broker.Submissions == 0);
        using var publicKey = RSA.Create();
        publicKey.ImportFromPem(f.Signing.ExportSubjectPublicKeyInfoPem());
        var evalCredentials = new CredentialService(new("phase8-supervisor", "phase8-evaluator", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), f.Clock);
        await using WebApplication eval = CollaborationApi.Build(["--urls", "http://127.0.0.1:0"], evalCredentials, evaluator: new IndependentEvaluator(publicKey, "demo-run", "researcher", "writer", "42", f.Clock), allowLoopbackHttp: true);
        await eval.StartAsync();
        using var evalHttp = new HttpClient
        {
            BaseAddress = new Uri(eval.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        using var evalRequest = new HttpRequestMessage(HttpMethod.Post, "/operator/challenge");
        evalRequest.Headers.Authorization = new("Bearer", f.Tokens["researcher"]);
        using HttpResponseMessage evalResponse = await evalHttp.SendAsync(evalRequest);
        Assert(evalResponse.StatusCode == HttpStatusCode.Unauthorized);
        // Even an evaluator-audience worker cannot reach the scorer's operator routes.
        using var roleRequest = new HttpRequestMessage(HttpMethod.Post, "/operator/challenge");
        roleRequest.Headers.Authorization = new("Bearer", evalCredentials.Issue("writer", "worker", TimeSpan.FromMinutes(1), "unused"));
        using HttpResponseMessage roleResponse = await evalHttp.SendAsync(roleRequest);
        Assert(roleResponse.StatusCode == HttpStatusCode.Forbidden);
        f.Clock.Advance(TimeSpan.FromMinutes(3));
        await Status(f.Tokens["researcher"], "/worker/send", body, HttpStatusCode.Unauthorized);
        Assert(f.Broker.Delivered == 0);
        await eval.StopAsync();
        await app.StopAsync();
    }
}
