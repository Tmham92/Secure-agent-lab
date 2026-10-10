using System.Diagnostics;
using System.Text.Json;
using SecureAgentLab.Durable.Persistence;

namespace SecureAgentLab.Comparisons.Evidence;

public static class ComparisonEvidence
{
    public static void Require(bool value, string reason)
    {
        if (!value)
        {
            throw new InvalidOperationException("Comparison failed: " + reason);
        }
    }

    public static Observation Observe(string variant, string decision, bool positive, params (string Name, object Value)[] effects) => new(variant, decision, effects.ToDictionary(p => p.Name, p => p.Value), positive);
    public static Comparison Pair(string id, string safeguard, string input, Func<(Observation Unsafe, Observation Secure)> execute)
    {
        var watch = Stopwatch.StartNew();
        (Observation Unsafe, Observation Secure) result = execute();
        Require(result.Unsafe.PositiveControl && result.Secure.PositiveControl, "legitimate positive control");
        return new(id, safeguard, input, DurableFiles.Hash(input), result.Unsafe, result.Secure, true, watch.ElapsedMilliseconds);
    }

    public static void Save(string root, IEnumerable<Comparison> pairs)
    {
        Comparison[] list = pairs.ToArray();
        File.WriteAllText(Path.Combine(root, "comparisons.json"), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        var lines = new List<string>
        {
            "# Safeguard comparison results",
            "",
            "Synthetic effects only. PASS requires the unsafe failure, absence of the secure failure and legitimate positive controls.",
            "",
            "| Scenario | Unsafe observation | Secure observation | Result |",
            "|---|---|---|---|"
        };
        foreach (Comparison? p in list)
        {
            lines.Add($"| {p.Scenario} | {JsonSerializer.Serialize(p.Unsafe.Effects)} | {JsonSerializer.Serialize(p.Secure.Effects)} | PASS |");
        }

        File.WriteAllLines(Path.Combine(root, "summary.md"), lines);
    }
}
