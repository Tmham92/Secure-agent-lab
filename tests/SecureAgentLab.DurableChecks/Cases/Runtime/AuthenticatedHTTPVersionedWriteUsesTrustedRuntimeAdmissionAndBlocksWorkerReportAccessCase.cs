using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Fixtures;
using SecureAgentLab.Transport.Configuration;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;

namespace SecureAgentLab.DurableChecks.Cases.Runtime;

internal static class AuthenticatedHTTPVersionedWriteUsesTrustedRuntimeAdmissionAndBlocksWorkerReportAccessCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Contracts.Proposal draft, Func<Fixture> Make)
    {
        Fixture f = Make();
        DurableGateway g = Phase7Checks.Open(f);
        var settings = new CredentialSettings("phase7", "gateway", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var credentials = new CredentialService(settings, f.Clock);
        await using WebApplication app = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Error", "--Lab:EnableRuntimeLimits", "true", "--Lab:StateDirectory", f.State], settings, f.Clock, g, true);
        await app.StartAsync();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };
        client.DefaultRequestHeaders.Authorization = new("Bearer", credentials.Issue("reviewer", "operator", TimeSpan.FromMinutes(5)));
        using HttpResponseMessage issued = await client.PostAsJsonAsync("/operator/runs", new IssueRunRequest());
        issued.EnsureSuccessStatusCode();
        IssuedRun run = (await issued.Content.ReadFromJsonAsync<IssuedRun>())!;
        using HttpResponseMessage approved = await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(draft));
        approved.EnsureSuccessStatusCode();
        string ticket = (await approved.Content.ReadFromJsonAsync<IssuedApproval>())!.Ticket;
        client.DefaultRequestHeaders.Authorization = new("Bearer", run.WorkerCredential);
        using HttpResponseMessage hidden = await client.GetAsync("/operator/report");
        Phase7Checks.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        using HttpResponseMessage written = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(draft, ticket, "http"));
        written.EnsureSuccessStatusCode();
        Phase7Checks.Equal("publication_approved", (await written.Content.ReadFromJsonAsync<Decision>())!.Reason);
        Phase7Checks.Equal(new ReportSnapshot(1, draft.Content), g.GetReport());
        byte[] key = HMACSHA256.HashData(settings.Validate(), System.Text.Encoding.UTF8.GetBytes("runtime-budget-v1"));
        Phase7Checks.Equal(999L, new DurableBudgetStore(Path.Combine(f.State, "runtime-budgets"), key, f.Clock).Snapshot(run.RunId).InputTokens);
        await app.StopAsync();
    }
}
