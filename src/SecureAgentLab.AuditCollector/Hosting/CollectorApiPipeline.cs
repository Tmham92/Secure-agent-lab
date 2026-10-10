using System.Security.Cryptography;
using System.Text;
using SecureAgentLab.AuditCollector.Configuration;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.AuditCollector.Hosting;

internal static class CollectorApiPipeline
{
    internal static void Use(WebApplication app, CollectorSettings settings, byte[] expected)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!context.Request.IsHttps && !(settings.AllowLoopbackHttp && context.Connection.RemoteIpAddress is { } remote && System.Net.IPAddress.IsLoopback(remote)))
            {
                context.Response.StatusCode = 400;
                return;
            }

            string header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || header.Length > 1024 || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(header[7..]))))
            {
                context.Response.StatusCode = 401;
                return;
            }

            try
            {
                await next(context);
            }
            catch (Exception e) when (e is AuthorizationDependencyException or IOException or System.Text.Json.JsonException or CryptographicException)
            {
                context.Response.StatusCode = 503;
            }
        });
    }
}
