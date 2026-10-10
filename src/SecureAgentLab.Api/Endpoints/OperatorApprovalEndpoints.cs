using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;

namespace SecureAgentLab.Api.Endpoints;

internal static class OperatorApprovalEndpoints
{
    internal static void Map(RouteGroupBuilder operators)
    {
        operators.MapPost("/runs/{run}/approval-preview", (string run, ApprovalRequest request, ILabGateway g) =>
        {
            if (g is not DurableGateway durable)
            {
                return Results.BadRequest();
            }

            try
            {
                return Results.Ok(durable.PreviewPublication(run, request.Proposal));
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict();
            }
        });
        operators.MapPost("/runs/{run}/approvals", (string run, ApprovalRequest request, ILabGateway g) =>
        {
            if (request.LifetimeSeconds is <= 0 or > 300)
            {
                return Results.BadRequest();
            }

            try
            {
                return Results.Ok(new IssuedApproval(g.ApprovePublication(run, request.Proposal, TimeSpan.FromSeconds(request.LifetimeSeconds))));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest();
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict();
            }
        });
    }
}
