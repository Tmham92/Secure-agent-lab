using SecureAgentLab.Comparisons;

if (args.FirstOrDefault() == "--container") { await ContainerScenarios.Run(args.Skip(1).ToArray()); return; }
var selected = args.Length == 0 ? "portable" : args[0];
if (args.Length > 1 || (selected != "portable" && !PortableScenarios.Names.Contains(selected))) throw new ArgumentException("Choose portable or a named fixed scenario; no user paths/payloads accepted.");
var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "SecureAgentLab.slnx"))) repo = repo.Parent;
if (repo is null) throw new InvalidOperationException("Repository build required");
var root = Path.Combine(repo.FullName, "artifacts", "comparisons", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var results = new List<Comparison>();
foreach (var scenario in selected == "portable" ? PortableScenarios.Names : [selected])
{
    var pair = PortableScenarios.Run(scenario, root); results.Add(pair);
    Console.WriteLine($"PASS {pair.Scenario}: unsafe failure observed; secure failure absent; positive controls passed");
}
ComparisonEvidence.Save(root, results);
Console.WriteLine($"Comparison pairs: {results.Count}/{results.Count} passed. Evidence: {root}");
