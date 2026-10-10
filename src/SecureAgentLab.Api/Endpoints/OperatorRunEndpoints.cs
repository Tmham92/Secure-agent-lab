using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Transport.Credentials;
using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;

namespace SecureAgentLab.Api.Endpoints;

internal static class OperatorRunEndpoints
{
    internal static void Map(RouteGroupBuilder operators, WebApplication app, IConfiguration configuration, DurableBudgetStore? runtime)
    {
        operators.MapPost("/runs", (IssueRunRequest request, ILabGateway g, CredentialService issuer, TimeProvider time) =>
        {
            if (request.LifetimeSeconds is <= 0 or > 300 || request.Calls is < 0 or > 1000 || request.ResponseBytes is < 0 or > 1_000_000)
            {
                app.Logger.LogWarning("Run creation rejected: invalid limits (calls={Calls}, bytes={Bytes}, lifetime={Lifetime}).", request.Calls, request.ResponseBytes, request.LifetimeSeconds);
                return Results.BadRequest(new
                {
                    error = "invalid_run_limits"
                });
            }

            try
            {
                DateTimeOffset expires = time.GetUtcNow().AddSeconds(request.LifetimeSeconds);
                var grant = new TaskGrant(configuration["Lab:PolicyVersion"] ?? "synthetic-v1", expires, request.Calls, request.ResponseBytes, request.Permissions ?? [new(Operation.ReadDocument, "documents/task"), new(Operation.PublishReport, "reports/draft")]);
                string run = g.CreateSession(grant);
                if (runtime is not null)
                {
                    try
                    {
                        runtime.Create(run, new(expires, Math.Max(1, request.Calls), 1000, 1000, 10000));
                    }
                    catch
                    {
                        ((DurableGateway)g).SignalRevoke(run);
                        throw;
                    }
                }

                return Results.Ok(new IssuedRun(run, issuer.Issue(run, "worker", TimeSpan.FromSeconds(request.LifetimeSeconds), grant.Fingerprint), grant.Fingerprint, expires));
            }
            catch (ArgumentException exception)
            {
                app.Logger.LogWarning("Run creation rejected: {Reason}; policy configured={PolicyConfigured}, permissions supplied={PermissionsSupplied}.", exception.Message, !string.IsNullOrWhiteSpace(configuration["Lab:PolicyVersion"] ?? "synthetic-v1"), request.Permissions is not null);
                return Results.BadRequest(new
                {
                    error = "invalid_run_grant"
                });
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict();
            }
        });
    }
}
