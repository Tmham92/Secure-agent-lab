using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using SecureAgentLab.Api.Authentication;
using SecureAgentLab.Api.Configuration;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Policy;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Transport.Configuration;
using SecureAgentLab.Transport.Credentials;

namespace SecureAgentLab.Api.Hosting;

internal static class GatewayServices
{
    internal static (bool LocalHttp, DurableBudgetStore? Runtime) Configure(WebApplicationBuilder builder, CredentialSettings? settings, TimeProvider? clock, ILabGateway? gateway, bool allowLoopbackHttp, LabOptions options)
    {
        bool localHttp = allowLoopbackHttp || builder.Configuration.GetValue<bool>("Lab:AllowLoopbackHttp");
        settings ??= new(builder.Configuration["Lab:Issuer"] ?? "", builder.Configuration["Lab:Audience"] ?? "", builder.Configuration["Lab:SigningKey"] ?? "");
        clock ??= TimeProvider.System;
        bool runtimeEnabled = options.RuntimeLimits;
        if ((runtimeEnabled || options.ReportWrites) && options.UseInMemory)
        {
            throw new ArgumentException("Phase 7 requires durable storage.");
        }

        var credentials = new CredentialService(settings, clock); // Validate before listening.
        string? documentRoot = options.DocumentRoot;
        IDocumentReader documents = string.IsNullOrEmpty(documentRoot) ? new SyntheticDocumentReader() : new FileDocumentReader(documentRoot, SyntheticDocuments.Catalog);
        if (gateway is null)
        {
            if (options.UseInMemory)
            {
                gateway = new Gateway(new DefaultPolicy(), new DocumentExecutor(documents), clock);
            }
            else
            {
                string Required(string name) => builder.Configuration[name] ?? throw new ArgumentException("Missing durable configuration: " + name);
                var sink = new HttpAuditCollector(Required("Lab:AuditUrl"), Required("Lab:AuditWriteKey"), localHttp);
                byte[] key = HMACSHA256.HashData(settings.Validate(), Encoding.UTF8.GetBytes("durable-state-pending-v1"));
                gateway = new DurableGateway(Required("Lab:StateDirectory"), sink, Required("Lab:AuditPublicKey"), Required("Lab:AuditStreamId"), key, options.PolicyVersion, clock, documents: documents, enableReportWrites: options.ReportWrites);
                builder.Services.AddSingleton(sink);
            }
        }

        DurableBudgetStore? runtime = null;
        if (runtimeEnabled)
        {
            if (gateway is not DurableGateway)
            {
                throw new ArgumentException("Runtime limits require a durable gateway.");
            }

            string runtimePath = builder.Configuration["Lab:StateDirectory"] ?? throw new ArgumentException("Missing runtime directory.");
            runtime = new DurableBudgetStore(Path.Combine(runtimePath, "runtime-budgets"), HMACSHA256.HashData(settings.Validate(), Encoding.UTF8.GetBytes("runtime-budget-v1")), clock);
        }

        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.Configure<KeyManagementOptions>(o => o.XmlRepository = new EphemeralKeyRepository());
        builder.Services.AddSingleton(credentials);
        builder.Services.AddSingleton<ILabGateway>(gateway);
        builder.Services.AddSingleton(clock);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.AddAuthentication("Lab").AddScheme<AuthenticationSchemeOptions, LabAuthentication>("Lab", _ =>
        {
        });
        builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()).AddPolicy("worker", p => p.RequireAuthenticatedUser().RequireRole("worker")).AddPolicy("operator", p => p.RequireAuthenticatedUser().RequireRole("operator"));
        return (localHttp, runtime);
    }
}
