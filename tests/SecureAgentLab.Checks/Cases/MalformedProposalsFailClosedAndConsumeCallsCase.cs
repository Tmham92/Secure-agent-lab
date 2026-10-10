using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class MalformedProposalsFailClosedAndConsumeCallsCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g, calls: 6);
        Proposal?[] proposals = [null, new((Operation)999, "documents/task"), new(Operation.ReadDocument, null!), new(Operation.ReadDocument, ""), new(Operation.ReadDocument, "  "), new(Operation.ReadDocument, "documents/task", -1)];
        foreach (Proposal? p in proposals)
        {
            Expect(g.Execute(run, p), Outcome.Denied, "malformed_proposal");
        }

        Expect(g.Execute(run, Read()), Outcome.Denied, "call_budget_exhausted");
        Effects(g, 0, 0);
    }
}
