using System.Text.Json;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Core.Execution;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Containers.ContainerScenarioTransport;

namespace SecureAgentLab.Comparisons.Scenarios.Containers;

internal static class IsolationScenario
{
    internal static async Task RunAsync(bool insecure)
    {
        using HttpClient relay = Client("http://127.0.0.1:8080");
        Require(await relay.GetStringAsync("/read") == Gateway.TaskDocument, "authorized relay read failed");
        bool exists = File.Exists("/fake/secret.txt");
        Require(exists == insecure, "wrong synthetic mount isolation");
        string content = exists ? File.ReadAllText("/fake/secret.txt") : ComparisonFixture.FakeSecret;
        Require(content == ComparisonFixture.FakeSecret, "non-synthetic fixture rejected");
        using HttpClient direct = Client("http://127.0.0.1:9000", 1);
        bool transferred = false;
        try
        {
            using HttpResponseMessage response = await direct.PostAsJsonAsync("/capture/" + (insecure ? "unsafe" : "secure"), new Capture(content));
            if (!insecure)
            {
                throw new InvalidOperationException("Direct capture returned HTTP; application denial is not network isolation");
            }

            response.EnsureSuccessStatusCode();
            transferred = true;
        }
        catch (Exception e) when (!insecure && e is HttpRequestException or TaskCanceledException)
        {
        }

        Require(transferred == insecure, "network isolation did not differ as expected");
        File.WriteAllText("/workspace/isolation.json", JsonSerializer.Serialize(new
        {
            Insecure = insecure,
            FakeMountPresent = exists,
            Transferred = transferred,
            PositiveRead = true
        }));
        Console.WriteLine(insecure ? "PASS unsafe: fake mount read and direct local capture transfer observed" : "PASS secure: fake mount absent, direct capture blocked, permitted relay read succeeds");
    }
}
