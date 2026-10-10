using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SecureAgentLab.Comparisons.Fixtures;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;
using static global::SecureAgentLab.Comparisons.Scenarios.Containers.ContainerScenarioTransport;

namespace SecureAgentLab.Comparisons.Scenarios.Containers;

internal static class ContainmentScenario
{
    internal static async Task RunAsync(bool insecure)
    {
        File.WriteAllText("/workspace/earlier", ComparisonFixture.Reviewed);
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false
        };
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        info.ArgumentList.Add("--container");
        info.ArgumentList.Add("late-child");
        using Process child = Process.Start(info) ?? throw new InvalidOperationException("Child did not start");
        try
        {
            await Until(() => File.Exists("/workspace/ready"));
            DateTimeOffset stopAck = DateTimeOffset.UtcNow;
            File.WriteAllText("/workspace/stop-ack", stopAck.ToString("O"));
            if (!insecure)
            {
                File.WriteAllText("/workspace/revoked", "revoked");
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            File.WriteAllText("/workspace/release", "release controlled late action");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            DateTimeOffset exitedAt = DateTimeOffset.UtcNow;
            bool late = File.Exists("/workspace/late");
            DateTimeOffset? lateAt = late ? DateTimeOffset.Parse(File.ReadAllText("/workspace/late")) : (DateTimeOffset?)null;
            Require(late == insecure && (!late || lateAt >= stopAck), "late effect/stop ordering incorrect");
            Require(File.ReadAllText("/workspace/earlier") == ComparisonFixture.Reviewed, "earlier effect incorrectly undone");
            File.WriteAllText("/workspace/containment.json", JsonSerializer.Serialize(new
            {
                Insecure = insecure,
                StopAck = stopAck,
                KillSignal = !insecure,
                Revoked = File.Exists("/workspace/revoked"),
                ChildExited = child.HasExited,
                ExitedAt = exitedAt,
                LateWrite = late,
                LateAt = lateAt,
                EarlierWritePreserved = true
            }));
            Console.WriteLine(insecure ? "PASS unsafe: flag-only stop acknowledged, child wrote later, earlier effect preserved" : "PASS secure: revoked and owned child terminated, no late write, earlier effect preserved");
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }

            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
