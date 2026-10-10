using System.Text;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ActualByteLimitsDefeatZeroEstimatesAndPreventEffectsCase
{
    internal static void Run()
    {
        var g = new Gateway();
        string run = Session(g, bytes: Encoding.UTF8.GetByteCount(Gateway.TaskDocument) - 1);
        Expect(g.Execute(run, Read(estimate: 0)), Outcome.Denied, "response_budget_exhausted");
        string publishRun = Session(g, bytes: Encoding.UTF8.GetByteCount(Gateway.PublicationResult) - 1);
        Expect(g.Execute(publishRun, Publish(), Approve(g, publishRun)), Outcome.Denied, "response_budget_exhausted");
        Effects(g, 0, 0);
    }
}
