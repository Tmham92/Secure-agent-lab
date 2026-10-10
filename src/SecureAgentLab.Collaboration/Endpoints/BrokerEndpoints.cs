using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.Collaboration.Grants;
using SecureAgentLab.Transport.Credentials;

namespace SecureAgentLab.Collaboration.Endpoints;

internal static class BrokerEndpoints
{
    internal static void Map(WebApplication app, global::SecureAgentLab.Collaboration.Broker.Broker broker)
    {
        Credential Caller(HttpContext c) => (Credential)c.Items["caller"]!;
        app.MapPost("/worker/send", (SendMessage request, HttpContext c) => broker.Execute(Caller(c), "send", send: request));
        app.MapPost("/worker/receive", (ReceiveMessage request, HttpContext c) => broker.Execute(Caller(c), "receive", receive: request));
        app.MapPost("/worker/submit", (SubmitAnswer request, HttpContext c) => broker.Execute(Caller(c), "submit", submit: request));
        foreach (string? channel in new[]
        {
            "cache",
            "artifacts",
            "logs"
        }

        )
        {
            string action = channel;
            app.MapPost("/worker/" + channel, (HttpContext c) => broker.Execute(Caller(c), action));
        }

        app.MapPost("/operator/agents", (AgentGrant grant) => new AgentCredential(broker.Register(grant)));
        app.MapPost("/operator/seal", (SealRequest request) => broker.Seal(request.Run, request.Challenge));
        app.MapPost("/operator/revoke", (RevokeAgent request) =>
        {
            broker.Revoke(request.Agent);
            return Results.Ok();
        });
        app.MapPost("/operator/stop", () =>
        {
            broker.Stop();
            return Results.Ok();
        });
    }
}
