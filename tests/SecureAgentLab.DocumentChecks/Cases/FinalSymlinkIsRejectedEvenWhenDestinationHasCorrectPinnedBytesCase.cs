using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class FinalSymlinkIsRejectedEvenWhenDestinationHasCorrectPinnedBytesCase
{
    internal static void Run(FixtureCallback Fixture, ReaderCallback Reader)
    {
        string root = Fixture();
        string outside = Fixture();
        string path = Path.Combine(root, "task", "task.txt");
        File.Delete(path);
        File.CreateSymbolicLink(path, Path.Combine(outside, "task", "task.txt"));
        CheckAssertions.Equal("document_unavailable", Reader(root).Read("documents/task", 4096).Reason);
    }
}
