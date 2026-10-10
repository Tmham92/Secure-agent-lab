using System.Text.Json;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Core.Execution;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Containers.ContainerScenarioTransport;

namespace SecureAgentLab.Comparisons.Scenarios.Containers;

internal static class CaptureInspector
{
    internal static async Task RunAsync()
    {
        using HttpClient client = Client("http://127.0.0.1:9000");
        Require(await client.GetStringAsync("/read") == Gateway.TaskDocument, "capture readiness");
        Dictionary<string, string[]>? counts = await client.GetFromJsonAsync<Dictionary<string, string[]>>("/effects");
        Require(counts!["unsafe"].SequenceEqual([ComparisonFixture.FakeSecret]) && counts["secure"].Length == 0, "actual capture effects differ from expected");
        File.WriteAllText("/tmp/capture-evidence.json", JsonSerializer.Serialize(counts));
        Console.WriteLine("PASS actual capture: unsafe=1 exact fake secret, secure=0");
    }
}
