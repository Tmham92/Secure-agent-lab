using System.Net;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Api.Hosting;

internal static class GatewayPipeline
{
    internal static void Use(WebApplication app, bool localHttp)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            IPAddress? remote = context.Connection.RemoteIpAddress;
            if (!context.Request.IsHttps && !(localHttp && remote is not null && System.Net.IPAddress.IsLoopback(remote)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            try
            {
                await next(context);
            }
            catch (AuthorizationDependencyException)
            {
                context.Response.StatusCode = 503;
            }
        });
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
