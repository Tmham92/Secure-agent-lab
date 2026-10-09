using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SecureAgentLab.Core;
using SecureAgentLab.Transport;
using SecureAgentLab.Durable;
using System.Security.Cryptography;
using System.Text;

namespace SecureAgentLab.Api;

public static class LabApi
{
    public static WebApplication Build(string[] args, CredentialSettings? settings = null,
        TimeProvider? clock = null, ILabGateway? gateway = null, bool allowLoopbackHttp = false)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        var localHttp = allowLoopbackHttp || builder.Configuration.GetValue<bool>("Lab:AllowLoopbackHttp");
        settings ??= new(builder.Configuration["Lab:Issuer"] ?? "",
            builder.Configuration["Lab:Audience"] ?? "", builder.Configuration["Lab:SigningKey"] ?? "");
        clock ??= TimeProvider.System;
        bool Flag(string name) => bool.TryParse(builder.Configuration[name], out var value) && value;
        var runtimeEnabled = Flag("Lab:EnableRuntimeLimits");
        if ((runtimeEnabled || Flag("Lab:EnableReportWrites")) && Flag("Lab:UseInMemory"))
            throw new ArgumentException("Phase 7 requires durable storage.");
        var credentials = new CredentialService(settings, clock); // Validate before listening.
        var documentRoot = builder.Configuration["Lab:DocumentRoot"];
        IDocumentReader documents = string.IsNullOrEmpty(documentRoot) ? new SyntheticDocumentReader()
            : new FileDocumentReader(documentRoot, SyntheticDocuments.Catalog);
        if (gateway is null)
        {
            if (builder.Configuration.GetValue<bool>("Lab:UseInMemory")) gateway = new Gateway(new DefaultPolicy(), new DocumentExecutor(documents), clock);
            else
            {
                string Required(string name) => builder.Configuration[name] ?? throw new ArgumentException("Missing durable configuration: " + name);
                var sink = new HttpAuditCollector(Required("Lab:AuditUrl"), Required("Lab:AuditWriteKey"), localHttp);
                var key = HMACSHA256.HashData(settings.Validate(), Encoding.UTF8.GetBytes("durable-state-pending-v1"));
                gateway = new DurableGateway(Required("Lab:StateDirectory"), sink, Required("Lab:AuditPublicKey"),
                    Required("Lab:AuditStreamId"), key, builder.Configuration["Lab:PolicyVersion"] ?? "synthetic-v1", clock, documents: documents,
                    enableReportWrites: Flag("Lab:EnableReportWrites"));
                builder.Services.AddSingleton(sink);
            }
        }
        DurableBudgetStore? runtime = null;
        if (runtimeEnabled)
        {
            if (gateway is not DurableGateway) throw new ArgumentException("Runtime limits require a durable gateway.");
            var runtimePath = builder.Configuration["Lab:StateDirectory"] ?? throw new ArgumentException("Missing runtime directory.");
            runtime = new DurableBudgetStore(Path.Combine(runtimePath, "runtime-budgets"),
                HMACSHA256.HashData(settings.Validate(), Encoding.UTF8.GetBytes("runtime-budget-v1")), clock);
        }
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.Configure<KeyManagementOptions>(o => o.XmlRepository = new EphemeralKeyRepository());
        builder.Services.AddSingleton(credentials);
        builder.Services.AddSingleton<ILabGateway>(gateway);
        builder.Services.AddSingleton(clock);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.AddAuthentication("Lab").AddScheme<AuthenticationSchemeOptions, LabAuthentication>("Lab", _ => { });
        builder.Services.AddAuthorization(o =>
        {
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            o.AddPolicy("worker", p => p.RequireAuthenticatedUser().RequireRole("worker"));
            o.AddPolicy("operator", p => p.RequireAuthenticatedUser().RequireRole("operator"));
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var remote = context.Connection.RemoteIpAddress;
            if (!context.Request.IsHttps && !(localHttp && remote is not null && System.Net.IPAddress.IsLoopback(remote)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            try { await next(context); }
            catch (AuthorizationDependencyException) { context.Response.StatusCode = 503; }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapPost("/worker/proposals", async (ExecuteRequest request, ClaimsPrincipal caller, ILabGateway g, HttpContext context) =>
        {
            // Run identity is derived exclusively from signed authentication, never request JSON.
            var run = caller.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var grant = g.GetGrant(run);
            if (grant is null || grant.Fingerprint != caller.FindFirstValue("grant")) return Results.Forbid();
            if (runtime is not null)
            {
                var controller = new BoundedRunController((DurableGateway)g, runtime, run, TimeSpan.FromSeconds(2));
                // Synthetic admission units, not provider billing or actual model token usage.
                var decision = await controller.RunAsync(new(1, 1, 1), ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(g.Execute(run, request.Proposal, request.ApprovalTicket, request.IdempotencyKey));
                }, context.RequestAborted);
                return Results.Ok(decision);
            }
            return Results.Ok(g.Execute(run, request.Proposal, request.ApprovalTicket, request.IdempotencyKey));
        }).RequireAuthorization("worker");
        var operators = app.MapGroup("/operator").RequireAuthorization("operator");
        operators.MapPost("/runs", (IssueRunRequest request, ILabGateway g, CredentialService issuer, TimeProvider time) =>
        {
            if (request.LifetimeSeconds is <= 0 or > 300 || request.Calls is < 0 or > 1000 ||
                request.ResponseBytes is < 0 or > 1_000_000) return Results.BadRequest();
            try
            {
                var expires = time.GetUtcNow().AddSeconds(request.LifetimeSeconds);
                var grant = new TaskGrant(builder.Configuration["Lab:PolicyVersion"] ?? "synthetic-v1", expires, request.Calls, request.ResponseBytes,
                    request.Permissions ?? [new(Operation.ReadDocument, "documents/task"), new(Operation.PublishReport, "reports/draft")]);
                var run = g.CreateSession(grant);
                if (runtime is not null)
                {
                    try { runtime.Create(run, new(expires, Math.Max(1, request.Calls), 1000, 1000, 10000)); }
                    catch { ((DurableGateway)g).SignalRevoke(run); throw; }
                }
                return Results.Ok(new IssuedRun(run, issuer.Issue(run, "worker", TimeSpan.FromSeconds(request.LifetimeSeconds), grant.Fingerprint),
                    grant.Fingerprint, expires));
            }
            catch (ArgumentException) { return Results.BadRequest(); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        });
        operators.MapPost("/runs/{run}/approval-preview", (string run, ApprovalRequest request, ILabGateway g) =>
        {
            if (g is not DurableGateway durable) return Results.BadRequest();
            try { return Results.Ok(durable.PreviewPublication(run, request.Proposal)); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        });
        operators.MapPost("/runs/{run}/approvals", (string run, ApprovalRequest request, ILabGateway g) =>
        {
            if (request.LifetimeSeconds is <= 0 or > 300) return Results.BadRequest();
            try { return Results.Ok(new IssuedApproval(g.ApprovePublication(run, request.Proposal, TimeSpan.FromSeconds(request.LifetimeSeconds)))); }
            catch (ArgumentException) { return Results.BadRequest(); }
            catch (InvalidOperationException) { return Results.Conflict(); }
        });
        operators.MapPost("/runs/{run}/revoke", (string run, ILabGateway g) =>
        {
            if (g is DurableGateway durable) durable.SignalRevoke(run);
            runtime?.Revoke(run); g.Revoke(run); return Results.NoContent();
        });
        operators.MapPost("/stop", (ILabGateway g) =>
        {
            if (g is DurableGateway durable) durable.SignalStop();
            runtime?.Stop(); g.Stop(); return Results.NoContent();
        });
        operators.MapGet("/audit", (ILabGateway g) => Results.Ok(g.GetAuditSnapshot()));
        operators.MapGet("/effects", (ILabGateway g) => Results.Ok(g.GetEffects()));
        operators.MapGet("/report", (ILabGateway g) =>
        {
            if (g is not DurableGateway durable) return Results.BadRequest();
            try { return Results.Ok(durable.GetReport()); }
            catch (InvalidOperationException) { return Results.BadRequest(); }
        });
        return app;
    }
}

public sealed class LabAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, CredentialService credentials) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());
        var c = credentials.Validate(header[7..]);
        if (c is null) return Task.FromResult(AuthenticateResult.Fail("Invalid credential."));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, c.Subject), new(ClaimTypes.Role, c.Role) };
        if (c.GrantFingerprint is not null) claims.Add(new("grant", c.GrantFingerprint));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

// ASP.NET's key-ring warmup must also remain process-local; credentials do not use this key ring.
internal sealed class EphemeralKeyRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (elements) return elements.Select(e => new XElement(e)).ToArray();
    }
    public void StoreElement(XElement element, string friendlyName)
    {
        lock (elements) elements.Add(new XElement(element));
    }
}
