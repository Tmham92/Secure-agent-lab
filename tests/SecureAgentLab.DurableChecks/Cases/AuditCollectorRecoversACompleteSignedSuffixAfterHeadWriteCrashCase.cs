using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class AuditCollectorRecoversACompleteSignedSuffixAfterHeadWriteCrashCase
{
    internal static void Run(string privateKey, global::SecureAgentLab.Core.Contracts.Proposal read, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        f.Sink.Inner = new AuditCollectorStore(f.Log, f.Head, f.Stream, privateKey, _ => throw new SimulatedCrash());
        CheckAssertions.Throws<SimulatedCrash>(() => g.Execute(run, read));
        f.Sink.Inner = new AuditCollectorStore(f.Log, f.Head, f.Stream, privateKey);
        CheckAssertions.Equal(new MockEffects(1, 0), f.Open().GetEffects());
    }
}
