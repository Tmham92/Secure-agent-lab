using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class DeniedAttemptsExhaustCallBudgetCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g, calls: 2);
        Expect(g.Execute(run, Read("secrets")), Outcome.Denied, "out_of_scope");
        Expect(g.Execute(run, Publish()), Outcome.ApprovalRequired, "publication_approval_required");
        Expect(g.Execute(run, Read()), Outcome.Denied, "call_budget_exhausted");
        Effects(g, 0, 0);
    }
}
