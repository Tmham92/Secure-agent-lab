using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Scenarios.Containers;
using SecureAgentLab.Comparisons.Scenarios.Portable;

namespace SecureAgentLab.Comparisons.Hosting;

internal static class ComparisonsApplication
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.FirstOrDefault() == "--container")
        {
            await ContainerScenarios.Run(args.Skip(1).ToArray());
            return;
        }

        string selected = args.Length == 0 ? "portable" : args[0];
        if (args.Length > 1 || (selected != "portable" && !PortableScenarios.Names.Contains(selected)))
        {
            throw new ArgumentException("Choose portable or a named fixed scenario; no user paths/payloads accepted.");
        }

        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "SecureAgentLab.slnx")))
        {
            repo = repo.Parent;
        }

        if (repo is null)
        {
            throw new InvalidOperationException("Repository build required");
        }

        using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.Start(repo.FullName, "comparisons/portable");
        string root = artifactRun.DirectoryPath;
        Directory.CreateDirectory(root);
        var results = new List<Comparison>();
        foreach (string scenario in selected == "portable" ? PortableScenarios.Names : [selected])
        {
            Comparison pair = PortableScenarios.Run(scenario, root);
            results.Add(pair);
            Console.WriteLine($"PASS {pair.Scenario}: unsafe failure observed; secure failure absent; positive controls passed");
        }

        ComparisonEvidence.Save(root, results);
        Console.WriteLine($"Comparison pairs: {results.Count}/{results.Count} passed. Evidence: {root}");
    }
}
