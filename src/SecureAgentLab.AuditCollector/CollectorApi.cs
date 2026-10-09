using System.Security.Cryptography;
using System.Text;
using SecureAgentLab.Core;
using SecureAgentLab.Durable;

namespace SecureAgentLab.AuditCollector;

public sealed record CollectorSettings(string LogDirectory, string CheckpointDirectory, string StreamId,
    string SigningPrivateKey, string WriteKey, bool AllowLoopbackHttp = false);
public static class CollectorApi
{
    public static WebApplication Build(string[] args, CollectorSettings? settings = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        settings ??= new(Required("Audit:LogDirectory"), Required("Audit:CheckpointDirectory"), Required("Audit:StreamId"),
            Required("Audit:SigningPrivateKey"), Required("Audit:WriteKey"), builder.Configuration.GetValue<bool>("Audit:AllowLoopbackHttp"));
        string Required(string name) => builder.Configuration[name] ?? throw new ArgumentException("Missing audit configuration: " + name);
        if (Convert.FromBase64String(settings.WriteKey).Length < 32) throw new ArgumentException("Audit writer key must have at least 32 random bytes.");
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(settings.WriteKey));
        var store = new AuditCollectorStore(settings.LogDirectory, settings.CheckpointDirectory, settings.StreamId, settings.SigningPrivateKey);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 16 * 1024);
        builder.Services.AddSingleton<IAuditCollector>(store);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.IsHttps && !(settings.AllowLoopbackHttp && context.Connection.RemoteIpAddress is { } remote && System.Net.IPAddress.IsLoopback(remote)))
            { context.Response.StatusCode = 400; return; }
            var header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length > 1024 ||
                !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(header[7..]))))
            { context.Response.StatusCode = 401; return; }
            try { await next(context); }
            catch (Exception e) when (e is AuthorizationDependencyException or IOException or System.Text.Json.JsonException or CryptographicException)
            { context.Response.StatusCode = 503; }
        });
        app.MapGet("/checkpoint", (IAuditCollector sink) => sink.GetCheckpoint());
        app.MapGet("/events", (IAuditCollector sink) => sink.GetEvents());
        app.MapPost("/events", (AuditEntry entry, IAuditCollector sink) => sink.Append(entry));
        return app;
    }
}
