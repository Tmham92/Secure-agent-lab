using System.Security.Cryptography;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;

namespace SecureAgentLab.Comparisons.Fixtures;

public sealed class DurableFixture : IDisposable
{
    private readonly RSA signing = RSA.Create(2048);
    private readonly byte[] integrity = RandomNumberGenerator.GetBytes(32);
    private readonly string state;
    private readonly string stream = Guid.NewGuid().ToString("N");
    public AuditCollectorStore Collector
    {
        get;
    }
    public SwitchableSink Sink
    {
        get;
    }

    public DurableFixture(string path)
    {
        state = Path.Combine(path, "state");
        Collector = new(Path.Combine(path, "audit"), Path.Combine(path, "checkpoint"), stream, signing.ExportRSAPrivateKeyPem());
        Sink = new(Collector);
    }

    public DurableGateway Open(Action<DurabilityPoint>? fault = null) => new(state, Sink, Collector.PublicKey, stream, integrity, crash: fault, enableReportWrites: true);
    public static string Run(DurableGateway g) => g.CreateSession(TaskGrant.Default(DateTimeOffset.UtcNow.AddMinutes(3), 60, 65536));
    public bool Verify(AuditEntry[] entries)
    {
        SignedCheckpoint head = Collector.GetCheckpoint();
        new CheckpointVerifier(Collector.PublicKey, stream).Verify(head);
        return AuditChain.VerifyAudit(entries) && entries.Length == head.Value.Sequence && (entries.Length == 0 || entries[^1].Hash == head.Value.Hash);
    }

    public void Dispose() => signing.Dispose();
}
