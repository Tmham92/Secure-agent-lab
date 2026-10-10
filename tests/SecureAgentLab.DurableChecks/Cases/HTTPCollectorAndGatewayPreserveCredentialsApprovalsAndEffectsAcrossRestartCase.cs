using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using SecureAgentLab.Api;
using SecureAgentLab.AuditCollector;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Fixtures;
using SecureAgentLab.Transport.Configuration;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class HTTPCollectorAndGatewayPreserveCredentialsApprovalsAndEffectsAcrossRestartCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Contracts.Proposal draft, string privateKey, global::System.Security.Cryptography.RSA rsa, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        string writeKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using WebApplication collectorApp = CollectorApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Error"], new(f.Log, f.Head, f.Stream, privateKey, writeKey, true));
        await collectorApp.StartAsync();
        string collectorAddress = CheckAssertions.Address(collectorApp.Services);
        using var sink = new HttpAuditCollector(collectorAddress, writeKey, true);
        using var unauthenticated = new HttpClient
        {
            BaseAddress = new Uri(collectorAddress)
        };
        using HttpResponseMessage forbidden = await unauthenticated.GetAsync("/checkpoint");
        CheckAssertions.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
        var settings = new CredentialSettings("issuer", "audience", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var issuer = new CredentialService(settings, f.Clock);
        string token = issuer.Issue("operator", "operator", TimeSpan.FromMinutes(5));
        var durable = new DurableGateway(f.State, sink, rsa.ExportSubjectPublicKeyInfoPem(), f.Stream, f.IntegrityKey, clock: f.Clock);
        IssuedRun run;
        string approval;
        await using (WebApplication app = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Error"], settings, f.Clock, durable, true))
        {
            await app.StartAsync();
            using var client = new HttpClient
            {
                BaseAddress = new Uri(CheckAssertions.Address(app.Services))
            };
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            using HttpResponseMessage created = await client.PostAsJsonAsync("/operator/runs", new IssueRunRequest());
            created.EnsureSuccessStatusCode();
            run = (await created.Content.ReadFromJsonAsync<IssuedRun>())!;
            using HttpResponseMessage preview = await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approval-preview", new ApprovalRequest(draft));
            preview.EnsureSuccessStatusCode();
            CheckAssertions.Equal(draft.Content!, (await preview.Content.ReadFromJsonAsync<PublicationPreview>())!.Content);
            using HttpResponseMessage approved = await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(draft));
            approved.EnsureSuccessStatusCode();
            approval = (await approved.Content.ReadFromJsonAsync<IssuedApproval>())!.Ticket;
            client.DefaultRequestHeaders.Authorization = new("Bearer", run.WorkerCredential);
            using HttpResponseMessage denied = await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approval-preview", new ApprovalRequest(draft));
            CheckAssertions.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            await app.StopAsync();
        }

        durable = new DurableGateway(f.State, sink, rsa.ExportSubjectPublicKeyInfoPem(), f.Stream, f.IntegrityKey, clock: f.Clock);
        await using (WebApplication app = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Error"], settings, f.Clock, durable, true))
        {
            await app.StartAsync();
            using var client = new HttpClient
            {
                BaseAddress = new Uri(CheckAssertions.Address(app.Services))
            };
            client.DefaultRequestHeaders.Authorization = new("Bearer", run.WorkerCredential);
            using HttpResponseMessage response = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(draft, approval, "http-once"));
            response.EnsureSuccessStatusCode();
            CheckAssertions.Equal("publication_approved", (await response.Content.ReadFromJsonAsync<Decision>())!.Reason);
            using HttpResponseMessage replay = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(draft, approval, "http-once"));
            CheckAssertions.Equal("publication_replayed", (await replay.Content.ReadFromJsonAsync<Decision>())!.Reason);
            CheckAssertions.Equal(new MockEffects(0, 1), durable.GetEffects());
            await app.StopAsync();
        }

        await collectorApp.StopAsync();
        Decision failed = durable.Execute(run.RunId, draft, approval, "new-key");
        CheckAssertions.Equal("authorization_dependency_unavailable", failed.Reason);
    }
}
