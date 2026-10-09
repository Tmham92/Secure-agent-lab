using System.Net;
using System.Text.Json.Serialization;
using SecureAgentLab.Transport;

namespace SecureAgentLab.Collaboration;

public sealed record SealRequest(string Run, string Challenge);
public sealed record RevokeAgent(string Agent);
public sealed record AgentCredential(string Token);

public static class CollaborationApi
{
    public static WebApplication Build(string[] args, CredentialService credentials, Broker? broker = null,
        IndependentEvaluator? evaluator = null, bool allowLoopbackHttp = false)
    {
        if ((broker is null) == (evaluator is null)) throw new ArgumentException("Choose one isolated service role.");
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders(); // No worker-controlled message text in application/access logs.
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = evaluator is null ? 16 * 1024 : 2_100_000);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var remote = context.Connection.RemoteIpAddress;
            if (!context.Request.IsHttps && !(allowLoopbackHttp && remote is not null && IPAddress.IsLoopback(remote)))
            { context.Response.StatusCode = 400; return; }
            var header = context.Request.Headers.Authorization.ToString();
            var caller = header.StartsWith("Bearer ", StringComparison.Ordinal) ? credentials.Validate(header[7..]) : null;
            if (caller is null) { context.Response.StatusCode = 401; return; }
            var role = context.Request.Path.StartsWithSegments("/operator") ? "operator" : "worker";
            if (caller.Role != role) { context.Response.StatusCode = 403; return; }
            context.Items["caller"] = caller;
            try { await next(context); }
            catch (ArgumentException) { context.Response.StatusCode = 400; }
            catch (InvalidOperationException) { context.Response.StatusCode = 409; }
        });
        if (broker is not null)
        {
            Credential Caller(HttpContext c) => (Credential)c.Items["caller"]!;
            app.MapPost("/worker/send", (SendMessage request, HttpContext c) => broker.Execute(Caller(c), "send", send: request));
            app.MapPost("/worker/receive", (ReceiveMessage request, HttpContext c) => broker.Execute(Caller(c), "receive", receive: request));
            app.MapPost("/worker/submit", (SubmitAnswer request, HttpContext c) => broker.Execute(Caller(c), "submit", submit: request));
            foreach (var channel in new[] { "cache", "artifacts", "logs" })
            {
                var action = channel;
                app.MapPost("/worker/" + channel, (HttpContext c) => broker.Execute(Caller(c), action));
            }
            app.MapPost("/operator/agents", (AgentGrant grant) => new AgentCredential(broker.Register(grant)));
            app.MapPost("/operator/seal", (SealRequest request) => broker.Seal(request.Run, request.Challenge));
            app.MapPost("/operator/revoke", (RevokeAgent request) => { broker.Revoke(request.Agent); return Results.Ok(); });
            app.MapPost("/operator/stop", () => { broker.Stop(); return Results.Ok(); });
        }
        else
        {
            app.MapPost("/operator/challenge", () => evaluator!.Challenge());
            app.MapPost("/operator/evaluate", (SignedTranscript request) => evaluator!.Evaluate(request));
        }
        return app;
    }
}
