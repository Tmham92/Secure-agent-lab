using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;

namespace SecureAgentLab.Api.Endpoints;

internal static class OperatorControlEndpoints
{
    internal static void Map(RouteGroupBuilder operators, DurableBudgetStore? runtime)
    {
        operators.MapPost("/runs/{run}/revoke", (string run, ILabGateway g) =>
        {
            if (g is DurableGateway durable)
            {
                durable.SignalRevoke(run);
            }

            runtime?.Revoke(run);
            g.Revoke(run);
            return Results.NoContent();
        });
        operators.MapPost("/stop", (ILabGateway g) =>
        {
            if (g is DurableGateway durable)
            {
                durable.SignalStop();
            }

            runtime?.Stop();
            g.Stop();
            return Results.NoContent();
        });
    }
}
