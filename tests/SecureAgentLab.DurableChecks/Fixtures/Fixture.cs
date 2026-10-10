using System.Security.Cryptography;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
namespace SecureAgentLab.DurableChecks.Fixtures;

sealed class Fixture
{
    public string State
    {
        get;
    }
    public string Log
    {
        get;
    }
    public string Head
    {
        get;
    }
    public string Stream { get; } = Guid.NewGuid().ToString("N");
    public byte[] IntegrityKey { get; } = RandomNumberGenerator.GetBytes(32);
    public TestClock Clock { get; } = new();
    public SwitchableCollector Sink
    {
        get;
    }

    private readonly string _publicKey;
    public Fixture(string root, string privateKey)
    {
        root = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.FixturePath(root);
        State = Path.Combine(root, "gateway");
        Log = Path.Combine(root, "collector-log");
        Head = Path.Combine(root, "independent-head");
        var collector = new AuditCollectorStore(Log, Head, Stream, privateKey);
        _publicKey = collector.PublicKey;
        Sink = new(collector);
    }

    public DurableGateway Open(string version = "synthetic-v1", Action<DurabilityPoint>? crash = null) => new(State, Sink, _publicKey, Stream, IntegrityKey, version, Clock, crash);
    public string Run(DurableGateway g) => g.CreateSession(TaskGrant.Default(Clock.GetUtcNow().AddMinutes(5), 30, 4096));
}
