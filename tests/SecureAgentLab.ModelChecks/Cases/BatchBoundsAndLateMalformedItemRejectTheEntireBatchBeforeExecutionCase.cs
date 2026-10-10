using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class BatchBoundsAndLateMalformedItemRejectTheEntireBatchBeforeExecutionCase
{
    internal static void Run(string Valid, BytesCallback Bytes, RejectCallback Reject)
    {
        Reject("{\"proposals\":[]}");
        Reject("{\"proposals\":[" + string.Join(',', Enumerable.Repeat("{\"operation\":\"ReadDocument\",\"resource\":\"documents/task\"}", 9)) + "]}");
        var g = new Gateway();
        string run = g.CreateSession(TimeSpan.FromMinutes(1), 20, 4096);
        try
        {
            foreach (Proposal p in new ModelProposalSource(Bytes(Valid.Replace("]}", ",{\"operation\":\"Shell\",\"resource\":\"x\"}]}"))).GetProposals())
            {
                g.Execute(run, p);
            }
        }
        catch (ModelOutputException)
        {
        }
        CheckAssertions.
                Equal(new MockEffects(0, 0), g.GetEffects());
        CheckAssertions.Equal(20, g.GetSession(run)!.RemainingCalls);
    }
}
