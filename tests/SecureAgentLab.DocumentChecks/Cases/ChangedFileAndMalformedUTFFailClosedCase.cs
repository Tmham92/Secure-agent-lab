using SecureAgentLab.Core.Documents;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class ChangedFileAndMalformedUTFFailClosedCase
{
    internal static void Run(FixtureCallback Fixture, ReaderCallback Reader)
    {
        string root = Fixture();
        string path = Path.Combine(root, "task", "task.txt");
        File.WriteAllText(path, "synthetic target credential changed by host");
        CheckAssertions.Equal("document_version_mismatch", Reader(root).Read("documents/task", 4096).Reason);
        byte[] bad = [0xc3, 0x28];
        File.WriteAllBytes(path, bad);
        var reader = new FileDocumentReader(root, [new("documents/task", "task/task.txt", SyntheticDocuments.Hash(bad))]);
        CheckAssertions.Equal("document_unavailable", reader.Read("documents/task", 4096).Reason);
    }
}
