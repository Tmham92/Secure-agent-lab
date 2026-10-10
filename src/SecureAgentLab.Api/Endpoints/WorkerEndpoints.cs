using System.Security.Claims;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Transport.Requests;

namespace SecureAgentLab.Api.Endpoints;

internal static class WorkerEndpoints
{
    internal static void Map(WebApplication app, DurableBudgetStore? runtime)
    {
        app.MapPost("/worker/proposals", async (ExecuteRequest request, ClaimsPrincipal caller, ILabGateway g, HttpContext context) =>
        {
            // Run identity is derived exclusively from signed authentication, never request JSON.
            string run = caller.FindFirstValue(ClaimTypes.NameIdentifier)!;
            TaskGrant? grant = g.GetGrant(run);
            if (grant is null || grant.Fingerprint != caller.FindFirstValue("grant"))
            {
                return Results.Forbid();
            }

            if (runtime is not null)
            {
                var controller = new BoundedRunController((DurableGateway)g, runtime, run, TimeSpan.FromSeconds(2));
                // Synthetic admission units, not provider billing or actual model token usage.
                Decision decision = await controller.RunAsync(new(1, 1, 1), ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.FromResult(g.Execute(run, request.Proposal, request.ApprovalTicket, request.IdempotencyKey));
                }, context.RequestAborted);
                return Results.Ok(decision);
            }

            return Results.Ok(g.Execute(run, request.Proposal, request.ApprovalTicket, request.IdempotencyKey));
        }).RequireAuthorization("worker");
    }
}
