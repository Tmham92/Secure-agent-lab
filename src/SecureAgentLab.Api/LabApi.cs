using SecureAgentLab.Api.Configuration;
using SecureAgentLab.Api.Endpoints;
using SecureAgentLab.Api.Hosting;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Transport.Configuration;

namespace SecureAgentLab.Api;

public static class LabApi
{
    public static WebApplication Build(string[] args, CredentialSettings? settings = null, TimeProvider? clock = null, ILabGateway? gateway = null, bool allowLoopbackHttp = false)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        var options = LabOptions.From(builder.Configuration);
        (bool localHttp, DurableBudgetStore? runtime) = GatewayServices.Configure(builder, settings, clock, gateway, allowLoopbackHttp, options);
        WebApplication app = builder.Build();
        GatewayPipeline.Use(app, localHttp);
        WorkerEndpoints.Map(app, runtime);
        RouteGroupBuilder operators = app.MapGroup("/operator").RequireAuthorization("operator");
        OperatorRunEndpoints.Map(operators, app, app.Configuration, runtime);
        OperatorApprovalEndpoints.Map(operators);
        OperatorControlEndpoints.Map(operators, runtime);
        OperatorQueryEndpoints.Map(operators);
        return app;
    }
}
