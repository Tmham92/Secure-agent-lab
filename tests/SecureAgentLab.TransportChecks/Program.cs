using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Transport.Configuration;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
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
Add("Startup rejects absent authentication configuration", () =>
{
    Throws<ArgumentException>(() => new CredentialService(new("", "", settings.SigningKey)));
    Throws<ArgumentException>(() => new CredentialService(settings with { SigningKey = Convert.ToBase64String(new byte[8]) }));
    Throws<ArgumentNullException>(() => new Gateway(null!, new SyntheticExecutor()));
    return Task.CompletedTask;
});
Add("Immutable grants and scope validation", () =>
{
    ResourcePermission[] permissions = [new(Operation.ReadDocument, "documents/task")];
    var grant = new TaskGrant("v1", clock.GetUtcNow().AddMinutes(2), 3, 4096, permissions);
    permissions[0] = new(Operation.PublishReport, "reports/draft");
    var g = new Gateway(clock);
    string run = g.CreateSession(grant);
    Equal(Outcome.Allowed, g.Execute(run, read).Outcome);
    Equal(Outcome.Denied, g.Execute(run, publish).Outcome);
    Throws<InvalidOperationException>(() => g.ApprovePublication(run, publish, TimeSpan.FromMinutes(1)));
    var changed = new TaskGrant("v2", grant.ExpiresAt, 3, 4096, grant.Permissions);
    Assert(changed.Fingerprint != grant.Fingerprint, "Policy version missing from binding");
    Throws<ArgumentException>(() => new TaskGrant("v1", grant.ExpiresAt, 3, 4096, [new((Operation)999, "documents/task")]));
    Throws<ArgumentException>(() => new TaskGrant("v1", grant.ExpiresAt, 3, 4096, [new(Operation.ExternalRequest, "https://synthetic.invalid")]));
    Throws<ArgumentException>(() => new TaskGrant("v1", grant.ExpiresAt, 3, 4096, []));
    var executor = new SyntheticExecutor();
    Equal(Outcome.Denied, executor.Execute(new(Operation.ReadDocument, "secrets"), 4096).Outcome);
    Equal(new MockEffects(0, 0), executor.GetEffects());
    return Task.CompletedTask;
});
Add("Grant quota remains atomic under concurrency", () =>
{
    var g = new Gateway(clock);
    var grant = new TaskGrant("v1", clock.GetUtcNow().AddMinutes(2), 7, 100000, [new(Operation.ReadDocument, "documents/task")]);
    string run = g.CreateSession(grant);
    var outcomes = new Decision[100];
    Parallel.For(0, 100, i => outcomes[i] = g.Execute(run, read));
    Equal(7, outcomes.Count(d => d.Outcome == Outcome.Allowed));
    Equal(new MockEffects(7, 0), g.GetEffects());
    return Task.CompletedTask;
});
Add("Missing, forged, malformed and oversized credentials rejected before execution", async () =>
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
    Equal(before, gateway.GetEffects());
    Equal(audit, gateway.GetAuditSnapshot().Count);
});
Add("Wrong issuer, wrong audience and future credentials rejected", async () =>
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
    Equal(before, gateway.GetEffects());
});
Add("Signed unknown run and mismatched grant rejected before execution", async () =>
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

    Equal(before, gateway.GetEffects());
    Equal(audit, gateway.GetAuditSnapshot().Count);
});
Add("Worker cannot call any operator endpoint", async () =>
{
    IssuedRun run = await Issue();
    MockEffects before = gateway.GetEffects();
    await Status(run.WorkerCredential, "/operator/runs", new IssueRunRequest(), HttpStatusCode.Forbidden);
    await Status(run.WorkerCredential, $"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(publish), HttpStatusCode.Forbidden);
    await Status(run.WorkerCredential, $"/operator/runs/{run.RunId}/revoke", new
    {
    }, HttpStatusCode.Forbidden);
    await Status(run.WorkerCredential, "/operator/stop", new
    {
    }, HttpStatusCode.Forbidden);
    foreach (string? path in new[]
    {
        "/operator/audit",
        "/operator/effects"
    }

    )
    {
        using HttpResponseMessage response = await Send(run.WorkerCredential, HttpMethod.Get, path);
        Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    Equal(before, gateway.GetEffects());
    Equal(Outcome.Allowed, (await Execute(run, read)).Outcome); // Neither stop nor revoke happened.
    using HttpResponseMessage operatorWorkerCall = await Send(operatorToken, HttpMethod.Post, "/worker/proposals", new ExecuteRequest(read));
    Equal(HttpStatusCode.Forbidden, operatorWorkerCall.StatusCode);
});
Add("Caller identity cannot be injected through JSON", async () =>
{
    IssuedRun run = await Issue();
    MockEffects before = gateway.GetEffects();
    await Status(run.WorkerCredential, "/worker/proposals", new
    {
        proposal = read,
        runId = "forged"
    }, HttpStatusCode.BadRequest);
    Equal(before, gateway.GetEffects());
    Equal(Outcome.Allowed, (await Execute(run, read)).Outcome);
    Equal(run.RunId, gateway.GetAuditSnapshot()[^1].RunId);
});
Add("Operator cannot issue unknown or expanded grants", async () =>
{
    await Status(operatorToken, "/operator/runs", new IssueRunRequest(Permissions: [new(Operation.ExternalRequest, "outside")]), HttpStatusCode.BadRequest);
    await Status(operatorToken, "/operator/runs", new IssueRunRequest(LifetimeSeconds: 301), HttpStatusCode.BadRequest);
    IssuedRun run = await Issue(new IssueRunRequest(Permissions: [new(Operation.ReadDocument, "documents/task")]));
    Equal(Outcome.Denied, (await Execute(run, publish)).Outcome);
    await Status(operatorToken, $"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(publish), HttpStatusCode.Conflict);
});
Add("HTTP approval binding, one-time publication and revoke", async () =>
{
    IssuedRun run = await Issue();
    MockEffects before = gateway.GetEffects();
    Equal(Outcome.ApprovalRequired, (await Execute(run, publish)).Outcome);
    using HttpResponseMessage approvalResponse = await Send(operatorToken, HttpMethod.Post, $"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(publish));
    approvalResponse.EnsureSuccessStatusCode();
    string ticket = (await approvalResponse.Content.ReadFromJsonAsync<IssuedApproval>())!.Ticket;
    IssuedRun other = await Issue();
    Equal("approval_mismatch", (await Execute(other, publish, ticket)).Reason);
    Equal("publication_approved", (await Execute(run, publish, ticket)).Reason);
    Equal("approval_consumed", (await Execute(run, publish, ticket)).Reason);
    Equal(before.Publications + 1, gateway.GetEffects().Publications);
    await Status(operatorToken, $"/operator/runs/{run.RunId}/revoke", new
    {
    }, HttpStatusCode.NoContent);
    Equal("identity_revoked", (await Execute(run, read)).Reason);
});
Add("Separate worker process completes without signing key or operator credential", async () =>
{
    IssuedRun run = await Issue();
    string root = FindRepository();
    string config = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar) ? "Release" : "Debug";
    string workerDll = Path.Combine(root, "src", "SecureAgentLab.Worker", "bin", config, "net10.0", "SecureAgentLab.Worker.dll");
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    start.ArgumentList.Add(workerDll);
    foreach (string? key in start.Environment.Keys.Where(k => k.StartsWith("Lab__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("LAB_", StringComparison.OrdinalIgnoreCase)).ToArray())
    {
        start.Environment.Remove(key);
    }

    start.Environment["LAB_WORKER_CREDENTIAL"] = run.WorkerCredential;
    start.Environment["LAB_GATEWAY_URL"] = address;
    start.Environment["LAB_ALLOW_LOOPBACK_HTTP"] = "true";
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        process.Kill(entireProcessTree: true);
        throw;
    }

    Equal(0, process.ExitCode);
    string output = await stdout;
    string errors = await stderr;
    Assert(output.Contains("ReadDocument: Allowed") && output.Contains("PublishReport: ApprovalRequired"), "Missing worker walkthrough");
    Assert(!output.Contains(run.WorkerCredential) && !errors.Contains(run.WorkerCredential), "Credential leaked by worker");
});
Add("Expired credentials rejected at exact deadline", async () =>
{
    IssuedRun run = await Issue(new IssueRunRequest(LifetimeSeconds: 1));
    MockEffects before = gateway.GetEffects();
    clock.Advance(TimeSpan.FromSeconds(1));
    await Status(run.WorkerCredential, "/worker/proposals", new ExecuteRequest(read), HttpStatusCode.Unauthorized);
    Equal(before, gateway.GetEffects());
});
Add("Expired grant denied even with later valid credential", async () =>
{
    IssuedRun run = await Issue(new IssueRunRequest(LifetimeSeconds: 1));
    string token = issuer.Issue(run.RunId, "worker", TimeSpan.FromMinutes(1), run.GrantFingerprint);
    clock.Advance(TimeSpan.FromSeconds(1));
    MockEffects before = gateway.GetEffects();
    using HttpResponseMessage response = await Send(token, HttpMethod.Post, "/worker/proposals", new ExecuteRequest(read));
    response.EnsureSuccessStatusCode();
    Equal("identity_expired", (await response.Content.ReadFromJsonAsync<Decision>())!.Reason);
    Equal(before, gateway.GetEffects());
});
Add("HTTPS is required unless the loopback lab exception is explicit", async () =>
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
    Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Equal(new MockEffects(0, 0), strictGateway.GetEffects());
    await strict.StopAsync();
});
Add("HTTP malformed and unknown operations execute no tool", async () =>
{
    IssuedRun run = await Issue();
    MockEffects before = gateway.GetEffects();
    Equal("malformed_proposal", (await Execute(run, new((Operation)999, "documents/task"))).Reason);
    Equal("malformed_proposal", (await Execute(run, new(Operation.ReadDocument, "documents/task", -1))).Reason);
    Equal("out_of_scope", (await Execute(run, new(Operation.ReadDocument, "documents/../secrets"))).Reason);
    Equal(before, gateway.GetEffects());
});
Add("Operator stop blocks later worker execution and new grants", async () =>
{
    IssuedRun run = await Issue();
    MockEffects before = gateway.GetEffects();
    await Status(operatorToken, "/operator/stop", new
    {
    }, HttpStatusCode.NoContent);
    Equal("gateway_stopped", (await Execute(run, read)).Reason);
    await Status(operatorToken, "/operator/runs", new IssueRunRequest(), HttpStatusCode.Conflict);
    Equal(before, gateway.GetEffects());
    Assert(AuditChain.VerifyAudit(gateway.GetAuditSnapshot()), "Transport audit invalid");
});
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

static string FindRepository()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SecureAgentLab.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Repository not found");
}

static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<T>(Action action)
    where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}");
}
