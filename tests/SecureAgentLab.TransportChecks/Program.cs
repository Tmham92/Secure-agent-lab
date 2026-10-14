using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Transport.Configuration;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Cases;
using SecureAgentLab.TransportChecks.Fixtures;
using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.StartForAssembly("transport-checks");
var clock = new TestClock();
var settings = new CredentialSettings("lab-issuer", "lab-gateway", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
var issuer = new CredentialService(settings, clock);
var gateway = new Gateway(clock);
await using WebApplication app = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"], settings, clock, gateway, allowLoopbackHttp: true);
await app.StartAsync();
string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
{
    BaseAddress = new Uri(address),
    Timeout = TimeSpan.FromSeconds(15)
};
string operatorToken = issuer.Issue("operator", "operator", TimeSpan.FromMinutes(5));
var tests = new List<(string Name, Func<Task> Run)>();
var read = new Proposal(Operation.ReadDocument, "documents/task");
var publish = new Proposal(Operation.PublishReport, "reports/draft", 0);
Add("Startup rejects absent authentication configuration", () => StartupRejectsAbsentAuthenticationConfigurationCase.Run(settings));
Add("Immutable grants and scope validation", () => ImmutableGrantsAndScopeValidationCase.Run(clock, publish, read));
Add("Grant quota remains atomic under concurrency", () => GrantQuotaRemainsAtomicUnderConcurrencyCase.Run(clock, read));
Add("Missing, forged, malformed and oversized credentials rejected before execution", () => MissingForgedMalformedAndOversizedCredentialsRejectedBeforeExecutionCase.RunAsync(gateway, issuer, read, Status));
Add("Wrong issuer, wrong audience and future credentials rejected", () => WrongIssuerWrongAudienceAndFutureCredentialsRejectedCase.RunAsync(clock, gateway, read, settings, Status));
Add("Signed unknown run and mismatched grant rejected before execution", () => SignedUnknownRunAndMismatchedGrantRejectedBeforeExecutionCase.RunAsync(gateway, issuer, read, Issue, Status));
Add("Worker cannot call any operator endpoint", () => WorkerCannotCallAnyOperatorEndpointCase.RunAsync(gateway, operatorToken, publish, read, Execute, Issue, Send, Status));
Add("Caller identity cannot be injected through JSON", () => CallerIdentityCannotBeInjectedThroughJSONCase.RunAsync(gateway, read, Execute, Issue, Status));
Add("Operator cannot issue unknown or expanded grants", () => OperatorCannotIssueUnknownOrExpandedGrantsCase.RunAsync(operatorToken, publish, Execute, Issue, Status));
Add("HTTP approval binding, one-time publication and revoke", () => HTTPApprovalBindingOneTimePublicationAndRevokeCase.RunAsync(gateway, operatorToken, publish, read, Execute, Issue, Send, Status));
Add("Separate worker process completes without signing key or operator credential", () => SeparateWorkerProcessCompletesWithoutSigningKeyOrOperatorCredentialCase.RunAsync(address, Issue));
Add("Expired credentials rejected at exact deadline", () => ExpiredCredentialsRejectedAtExactDeadlineCase.RunAsync(clock, gateway, read, Issue, Status));
Add("Expired grant denied even with later valid credential", () => ExpiredGrantDeniedEvenWithLaterValidCredentialCase.RunAsync(clock, gateway, issuer, read, Issue, Send));
Add("HTTPS is required unless the loopback lab exception is explicit", () => HTTPSIsRequiredUnlessTheLoopbackLabExceptionIsExplicitCase.RunAsync(clock, operatorToken, settings));
Add("HTTP malformed and unknown operations execute no tool", () => HTTPMalformedAndUnknownOperationsExecuteNoToolCase.RunAsync(gateway, Execute, Issue));
Add("Operator stop blocks later worker execution and new grants", () => OperatorStopBlocksLaterWorkerExecutionAndNewGrantsCase.RunAsync(gateway, operatorToken, read, Execute, Issue, Status));
int failures = 0;
foreach ((string? name, Func<Task>? test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception e)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {e}");
    }
}

await app.StopAsync();
Console.WriteLine($"{tests.Count - failures}/{tests.Count} transport and grant checks passed.");
return failures == 0 ? 0 : 1;
void Add(string name, Func<Task> test) => tests.Add((name, test));
async Task<HttpResponseMessage> Send(string? token, HttpMethod method, string path, object? body = null)
{
    using var request = new HttpRequestMessage(method, path);
    if (token is not null)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    if (body is not null)
    {
        request.Content = JsonContent.Create(body);
    }

    return await http.SendAsync(request);
}

async Task Status(string? token, string path, object body, HttpStatusCode status)
{
    using HttpResponseMessage response = await Send(token, HttpMethod.Post, path, body);
    Equal(status, response.StatusCode);
}

async Task<IssuedRun> Issue(IssueRunRequest? body = null)
{
    using HttpResponseMessage response = await Send(operatorToken, HttpMethod.Post, "/operator/runs", body ?? new IssueRunRequest());
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<IssuedRun>())!;
}

async Task<Decision> Execute(IssuedRun run, Proposal proposal, string? ticket = null)
{
    using HttpResponseMessage response = await Send(run.WorkerCredential, HttpMethod.Post, "/worker/proposals", new ExecuteRequest(proposal, ticket));
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<Decision>())!;
}



static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
