using System.Text;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Core.Policy;
using SecureAgentLab.DocumentChecks.Cases;
var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "SecureAgentLab.slnx")))
{
    repo = repo.Parent;
}

using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.StartForAssembly("document-checks");
string basePath = artifactRun.DirectoryPath;
var checks = new List<(string Name, Func<Task> Run)>();
var read = new Proposal(Operation.ReadDocument, "documents/task", 0);
string Fixture()
{
    string root = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.FixturePath(basePath);
    Directory.CreateDirectory(Path.Combine(root, "task"));
    Directory.CreateDirectory(Path.Combine(root, "reference"));
    File.WriteAllText(Path.Combine(root, "task", "task.txt"), Gateway.TaskDocument, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(root, "reference", "reference.txt"), SyntheticDocuments.Reference, new UTF8Encoding(false));
    return root;
}

FileDocumentReader Reader(string root, int max = 4096) => new(root, SyntheticDocuments.Catalog, max);
Gateway GatewayFor(string root) => new(new DefaultPolicy(), new DocumentExecutor(Reader(root)));
string Grant(Gateway g, string id, long bytes = 4096) => g.CreateSession(new TaskGrant("synthetic-v1", DateTimeOffset.UtcNow.AddMinutes(3), 20, bytes, [new(Operation.ReadDocument, id)]));
void Add(string name, Action run) => checks.Add((name, () =>
{
    run();
    return Task.CompletedTask;
}
));
Add("Exact file read and measured multibyte response quota", () => ExactFileReadAndMeasuredMultibyteResponseQuotaCase.Run(read, Fixture, GatewayFor, Grant));
Add("Cross-task grants cannot read the other catalog document", () => CrossTaskGrantsCannotReadTheOtherCatalogDocumentCase.Run(read, Fixture, GatewayFor, Grant));
Add("Traversal, absolute paths, URLs, ADS and encoded aliases are rejected", () => TraversalAbsolutePathsURLsADSAndEncodedAliasesAreRejectedCase.Run(Fixture, GatewayFor, Grant, Reader));
Add("Host catalog rejects traversal, platform separators, aliases and invalid pins", () => HostCatalogRejectsTraversalPlatformSeparatorsAliasesAndInvalidPinsCase.Run(Fixture));
Add("Oversized documents never return partial content or charge response bytes", () => OversizedDocumentsNeverReturnPartialContentOrChargeResponseBytesCase.Run(read, Fixture, GatewayFor, Grant));
Add("Zero estimate cannot bypass actual byte limit", () => ZeroEstimateCannotBypassActualByteLimitCase.Run(read, Fixture, GatewayFor, Grant));
Add("Changed file and malformed UTF-8 fail closed", () => ChangedFileAndMalformedUTFFailClosedCase.Run(Fixture, Reader));
Add("Missing executor file denies without paths, secret bytes or read effects", () => MissingExecutorFileDeniesWithoutPathsSecretBytesOrReadEffectsCase.Run(read, Fixture, GatewayFor, Grant));
Add("Hostile document text remains data and cannot enable operations", () => HostileDocumentTextRemainsDataAndCannotEnableOperationsCase.Run(Fixture, GatewayFor, Grant));
Add("Read proposal cannot supply alternate content", () => ReadProposalCannotSupplyAlternateContentCase.Run(read, Fixture, GatewayFor, Grant));
Add("Executor-only credential canary is outside catalog and never returned", () => ExecutorOnlyCredentialCanaryIsOutsideCatalogAndNeverReturnedCase.Run(read, Fixture, GatewayFor, Grant));
Add("Directory junction/symlink and root alias are rejected", () => DirectoryJunctionSymlinkAndRootAliasAreRejectedCase.Run(Fixture, Reader));
if (OperatingSystem.IsLinux())
{
    Add("Final symlink is rejected even when destination has correct pinned bytes", () => FinalSymlinkIsRejectedEvenWhenDestinationHasCorrectPinnedBytesCase.Run(Fixture, Reader));
    Add("FIFO is rejected without blocking", () => FIFOIsRejectedWithoutBlockingCase.Run(Fixture, Reader));
}

Add("Durable reader preserves measured quotas across restart and redacts document bodies", () => DurableReaderPreservesMeasuredQuotasAcrossRestartAndRedactsDocumentBodiesCase.Run(read, Fixture, Reader));
checks.Add(("Authenticated HTTP uses file adapter and derives scope from identity", () => AuthenticatedHTTPUsesFileAdapterAndDerivesScopeFromIdentityCase.RunAsync(read, Fixture, GatewayFor, Grant)));
int failures = 0;
foreach ((string? name, Func<Task>? run) in checks)
{
    try
    {
        await run();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception e)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {e}");
    }
}

Console.WriteLine($"{checks.Count - failures}/{checks.Count} document checks passed. Linux adds final-symlink and FIFO checks.");
return failures == 0 ? 0 : 1;
