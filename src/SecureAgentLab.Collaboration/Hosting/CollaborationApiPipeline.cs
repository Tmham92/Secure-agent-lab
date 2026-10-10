using System.Net;
using SecureAgentLab.Transport.Credentials;

namespace SecureAgentLab.Collaboration.Hosting;

internal static class CollaborationApiPipeline
{
    internal static void Use(WebApplication app, CredentialService credentials, bool allowLoopbackHttp)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            IPAddress? remote = context.Connection.RemoteIpAddress;
            if (!context.Request.IsHttps && !(allowLoopbackHttp && remote is not null && IPAddress.IsLoopback(remote)))
            {
                context.Response.StatusCode = 400;
                return;
            }

            string header = context.Request.Headers.Authorization.ToString();
            Credential? caller = header.StartsWith("Bearer ", StringComparison.Ordinal) ? credentials.Validate(header[7..]) : null;
            if (caller is null)
            {
                context.Response.StatusCode = 401;
                return;
            }

            string role = context.Request.Path.StartsWithSegments("/operator") ? "operator" : "worker";
            if (caller.Role != role)
            {
                context.Response.StatusCode = 403;
                return;
            }

            context.Items["caller"] = caller;
            try
            {
                await next(context);
            }
            catch (ArgumentException)
            {
                context.Response.StatusCode = 400;
            }
            catch (InvalidOperationException)
            {
                context.Response.StatusCode = 409;
            }
        });
    }
}
