using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;

namespace SecureAgentLab.Api.Endpoints;

internal static class OperatorQueryEndpoints
{
    internal static void Map(RouteGroupBuilder operators)
    {
        operators.MapGet("/audit", (ILabGateway g) => Results.Ok(g.GetAuditSnapshot()));
        operators.MapGet("/effects", (ILabGateway g) => Results.Ok(g.GetEffects()));
        operators.MapGet("/report", (ILabGateway g) =>
        {
            if (g is not DurableGateway durable)
            {
                return Results.BadRequest();
            }

            try
            {
                return Results.Ok(durable.GetReport());
            }
            catch (InvalidOperationException)
            {
                return Results.BadRequest();
            }
        });
    }
}
