using SecureAgentLab.Core.Documents;
using SecureAgentLab.DocumentChecks.Fixtures;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class HostCatalogRejectsTraversalPlatformSeparatorsAliasesAndInvalidPinsCase
{
    internal static void Run(FixtureCallback Fixture)
    {
        string root = Fixture();
        foreach (string? path in new[]
        {
            "../task.txt",
            "/task.txt",
            "task/../task.txt",
            "task\\task.txt",
            "task//task.txt",
            "task/task.txt:stream",
            "task/%74ask.txt"
        }

        )
        {
            CheckAssertions.Throws<ArgumentException>(() => new FileDocumentReader(root, [new("documents/task", path, new string('A', 64))]));
        }
        CheckAssertions.
                Throws<ArgumentException>(() => new FileDocumentReader(root, [new("documents/private", "task/task.txt", new string('A', 64))]));
        CheckAssertions.Throws<ArgumentException>(() => new FileDocumentReader(root, [new("documents/task", "task/task.txt", "invalid")]));
    }
}
