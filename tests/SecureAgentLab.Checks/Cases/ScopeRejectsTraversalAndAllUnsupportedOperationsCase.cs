using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ScopeRejectsTraversalAndAllUnsupportedOperationsCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g);
        foreach (string? resource in new[]
        {
            "documents/../secrets",
            "documents/task/",
            "Documents/task",
            "documents/other",
            " documents/task",
            "documents/task\0"
        }

        )
        {
            Expect(g.Execute(run, Read(resource)), Outcome.Denied, "out_of_scope");
        }

        foreach (Operation op in new[]
        {
            Operation.ExternalRequest,
            Operation.MessageAgent,
            Operation.ChangePermissions
        }

        )
        {
            Expect(g.Execute(run, new(op, "documents/task")), Outcome.Denied, "out_of_scope");
        }

        Expect(g.Execute(run, Publish("reports/other")), Outcome.Denied, "out_of_scope");
        Effects(g, 0, 0);
    }
}
