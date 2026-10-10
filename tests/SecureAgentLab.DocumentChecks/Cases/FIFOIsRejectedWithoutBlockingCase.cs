using System.Diagnostics;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class FIFOIsRejectedWithoutBlockingCase
{
    internal static void Run(FixtureCallback Fixture, ReaderCallback Reader)
    {
        string root = Fixture();
        string path = Path.Combine(root, "task", "task.txt");
        File.Delete(path);
        using Process process = Process.Start(new ProcessStartInfo("mkfifo") { ArgumentList = { path } })!;
        process.WaitForExit();
        CheckAssertions.Equal(0, process.ExitCode);
        CheckAssertions.Equal("document_unavailable", Reader(root).Read("documents/task", 4096).Reason);
    }
}
