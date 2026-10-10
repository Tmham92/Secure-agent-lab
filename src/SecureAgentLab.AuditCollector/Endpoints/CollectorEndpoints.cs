using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Audit;

namespace SecureAgentLab.AuditCollector.Endpoints;

internal static class CollectorEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/checkpoint", (IAuditCollector sink) => sink.GetCheckpoint());
        app.MapGet("/events", (IAuditCollector sink) => sink.GetEvents());
        app.MapPost("/events", (AuditEntry entry, IAuditCollector sink) => sink.Append(entry));
    }
}
