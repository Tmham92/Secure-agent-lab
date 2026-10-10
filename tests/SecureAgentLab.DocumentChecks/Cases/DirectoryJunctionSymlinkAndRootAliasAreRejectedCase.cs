using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class DirectoryJunctionSymlinkAndRootAliasAreRejectedCase
{
    internal static void Run(FixtureCallback Fixture, ReaderCallback Reader)
    {
        string root = Fixture();
        string outside = Fixture();
        File.Delete(Path.Combine(root, "task", "task.txt"));
        Directory.Delete(Path.Combine(root, "task"));
        CheckAssertions.LinkDirectory(Path.Combine(root, "task"), Path.Combine(outside, "task"));
        CheckAssertions.Equal("document_unavailable", Reader(root).Read("documents/task", 4096).Reason);
        string alias = root + "-alias";
        CheckAssertions.LinkDirectory(alias, outside);
        CheckAssertions.Equal("document_unavailable", Reader(alias).Read("documents/task", 4096).Reason);
    }
}
