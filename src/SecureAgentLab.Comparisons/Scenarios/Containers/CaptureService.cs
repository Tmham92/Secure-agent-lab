using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Core.Execution;

namespace SecureAgentLab.Comparisons.Scenarios.Containers;

internal static class CaptureService
{
    internal static async Task RunAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024);
        WebApplication app = builder.Build();
        object gate = new object();
        var capture = new Dictionary<string, List<string>>
        {
            ["unsafe"] = [],
            ["secure"] = []
        };
        app.MapGet("/read", () => Gateway.TaskDocument);
        app.MapPost("/capture/{variant}", (string variant, Capture body) =>
        {
            lock (gate)
            {
                if (!capture.TryGetValue(variant, out List<string>? bucket) || body.Text != ComparisonFixture.FakeSecret || bucket.Count >= 1)
                {
                    return Results.BadRequest();
                }

                bucket.Add(body.Text);
                return Results.Ok();
            }
        });
        app.MapGet("/effects", () =>
        {
            lock (gate)
            {
                return capture.ToDictionary(p => p.Key, p => p.Value.ToArray());
            }
        });
        await app.RunAsync();
    }
}
