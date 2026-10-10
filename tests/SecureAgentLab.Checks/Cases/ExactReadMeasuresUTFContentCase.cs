using System.Text;
using SecureAgentLab.Checks.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using static SecureAgentLab.Checks.Fixtures.CheckAssertions;

namespace SecureAgentLab.Checks.Cases;

internal static class ExactReadMeasuresUTFContentCase
{
    internal static void Run()
    {
        var g = new Gateway();
        int bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
        string run = Session(g, bytes: bytes);
        Decision d = g.Execute(run, Read());
        Expect(d, Outcome.Allowed, "scoped_read");
        CheckAssertions.Equal(Gateway.TaskDocument, d.Result);
        CheckAssertions.Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        Effects(g, 1, 0);
    }
}
