using System.Text;
using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class CumulativeResponseQuotaAndPublicationAccountingCase
{
    internal static void Run()
    {
        var g = new Gateway();
        int bytes = Encoding.UTF8.GetByteCount(Gateway.PublicationResult);
        string run = Session(g, bytes: bytes);
        Expect(g.Execute(run, Publish(), Approve(g, run)), Outcome.Allowed, "publication_approved");
        CheckAssertions.Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        Expect(g.Execute(run, Publish(), Approve(g, run)), Outcome.Denied, "response_budget_exhausted");
        Effects(g, 0, 1);
    }
}
