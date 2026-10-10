using System.Text;
using SecureAgentLab.Comparisons.Evidence;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Comparisons.Vulnerable;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using static global::SecureAgentLab.Comparisons.Evidence.ComparisonEvidence;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class FilesScenario
{
    internal static Comparison Run(string root) => Pair("6-files", "Exact resource IDs safe opens and measured size", "../private.txt; link/secret.txt; declared-zero 256-byte file with 128-byte cap", () =>
    {
        (ComparisonFixture Unsafe, ComparisonFixture Secure) f = PortableScenarioSupport.Fixtures(root, "files");
        foreach (ComparisonFixture? fixture in new[]
        {
            f.Unsafe,
            f.Secure
        }

        )
        {
            fixture.Link();
            File.WriteAllText(Path.Combine(fixture.Documents, "task", "large.txt"), new string('x', 256));
        }

        string traversal = DeliberatelyVulnerable.ReadPath(f.Unsafe, "../private.txt", 0);
        string linked = DeliberatelyVulnerable.ReadPath(f.Unsafe, "link/secret.txt", 0);
        string oversize = DeliberatelyVulnerable.ReadPath(f.Unsafe, "task/large.txt", 0);
        var reader = new FileDocumentReader(f.Secure.Documents, SyntheticDocuments.Catalog, 128);
        Decision denied = reader.Read("../private.txt", 4096);
        // A trusted catalog accidentally referencing a link is still rejected at open, independently of resource scope.
        var linkReader = new FileDocumentReader(f.Secure.Documents, [new("documents/reference", "link/secret.txt", SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(ComparisonFixture.FakeSecret)))]);
        Decision linkDenied = linkReader.Read("documents/reference", 4096);
        var sizeReader = new FileDocumentReader(f.Secure.Documents, [new("documents/reference", "task/large.txt", SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(oversize)))], 128);
        Decision sizeDenied = sizeReader.Read("documents/reference", 4096);
        Require(traversal == ComparisonFixture.FakeSecret && linked == ComparisonFixture.FakeSecret && oversize.Length == 256, "unsafe file failures absent");
        Require(new[] { denied, linkDenied, sizeDenied }.All(d => d.Outcome == Outcome.Denied && d.Result is null), "secure file returned partial forbidden bytes");
        return (Observe("unsafe", "caller paths links and declared size trusted", DeliberatelyVulnerable.ReadPath(f.Unsafe, "task/task.txt", 0) == Gateway.TaskDocument, ("traversalLeak", traversal), ("linkLeak", linked), ("oversizeBytes", Encoding.UTF8.GetByteCount(oversize))), Observe("secure", "all three file failures denied", reader.Read("documents/task", 4096).Result == Gateway.TaskDocument, ("traversalBytes", 0), ("linkBytes", 0), ("oversizeBytes", 0)));
    });
}
