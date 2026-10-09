using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Core;
using SecureAgentLab.Durable;

namespace SecureAgentLab.Comparisons;

public sealed record Observation(string Variant, string Decision, Dictionary<string, object> Effects, bool PositiveControl);
public sealed record Comparison(string Scenario, string MissingSafeguard, string Input, string InputSha256,
    Observation Unsafe, Observation Secure, bool Passed, long ElapsedMilliseconds);

// No user-supplied paths, targets, payloads, model outputs or commands are accepted by this executable.
public sealed class ComparisonFixture
{
    public const string FakeSecret = "FAKE_SECRET_COMPARISON_ONLY";
    public const string Reviewed = "Reviewed harmless synthetic draft";
    public const string Replaced = "Unreviewed harmless synthetic replacement";
    public string Directory { get; }
    public string Documents => Path.Combine(Directory, "documents");
    public ComparisonFixture(string root, string scenario, string variant)
    {
        Directory = Path.Combine(root, scenario, variant);
        System.IO.Directory.CreateDirectory(Path.Combine(Documents, "task"));
        System.IO.Directory.CreateDirectory(Path.Combine(Documents, "reference"));
        File.WriteAllText(Path.Combine(Documents, "task", "task.txt"), Gateway.TaskDocument);
        File.WriteAllText(Path.Combine(Documents, "reference", "reference.txt"), SyntheticDocuments.Reference);
        File.WriteAllText(Path.Combine(Directory, "private.txt"), FakeSecret);
    }
    public Gateway ReaderGateway() => new(new DefaultPolicy(), new DocumentExecutor(new FileDocumentReader(Documents, SyntheticDocuments.Catalog)));
    public static string Run(Gateway g) => g.CreateSession(TaskGrant.Default(DateTimeOffset.UtcNow.AddMinutes(3), 30, 8192));
    public void Link()
    {
        var target = Path.Combine(Directory, "private"); System.IO.Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "secret.txt"), FakeSecret);
        var link = Path.Combine(Documents, "link");
        if (!OperatingSystem.IsWindows()) { System.IO.Directory.CreateSymbolicLink(link, target); return; }
        // A directory junction needs no symlink privilege. Both paths are generated within this fixture.
        if ((link + target).Any(c => c is '"' or '&' or '|' or '<' or '>' or '%' or '^' or '\n' or '\r')) throw new InvalidOperationException("Unsafe fixture path");
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"";
        using var p = Process.Start(info)!;
        if (!p.WaitForExit(5000) || p.ExitCode != 0) throw new InvalidOperationException("Cannot create synthetic junction fixture");
    }
}

public sealed class DurableFixture : IDisposable
{
    private readonly RSA signing = RSA.Create(2048);
    private readonly byte[] integrity = RandomNumberGenerator.GetBytes(32);
    private readonly string state;
    private readonly string stream = Guid.NewGuid().ToString("N");
    public AuditCollectorStore Collector { get; }
    public SwitchableSink Sink { get; }
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
        var head = Collector.GetCheckpoint(); new CheckpointVerifier(Collector.PublicKey, stream).Verify(head);
        return AuditChain.VerifyAudit(entries) && entries.Length == head.Value.Sequence && (entries.Length == 0 || entries[^1].Hash == head.Value.Hash);
    }
    public void Dispose() => signing.Dispose();
}
public sealed class SwitchableSink(IAuditCollector inner) : IAuditCollector
{
    public bool LoseAck { get; set; }
    public bool Offline { get; set; }
    private void Ready() { if (Offline) throw new AuthorizationDependencyException("synthetic_sink_outage"); }
    public SignedCheckpoint GetCheckpoint() { Ready(); return inner.GetCheckpoint(); }
    public IReadOnlyList<SignedAuditEvent> GetEvents() { Ready(); return inner.GetEvents(); }
    public SignedCheckpoint Append(AuditEntry e)
    {
        Ready(); var result = inner.Append(e);
        if (LoseAck) throw new AuthorizationDependencyException("synthetic_lost_ack");
        return result;
    }
}

public static class ComparisonEvidence
{
    public static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException("Comparison failed: " + reason); }
    public static Observation Observe(string variant, string decision, bool positive, params (string Name, object Value)[] effects) =>
        new(variant, decision, effects.ToDictionary(p => p.Name, p => p.Value), positive);
    public static Comparison Pair(string id, string safeguard, string input, Func<(Observation Unsafe, Observation Secure)> execute)
    {
        var watch = Stopwatch.StartNew(); var result = execute();
        Require(result.Unsafe.PositiveControl && result.Secure.PositiveControl, "legitimate positive control");
        return new(id, safeguard, input, DurableFiles.Hash(input), result.Unsafe, result.Secure, true, watch.ElapsedMilliseconds);
    }
    public static void Save(string root, IEnumerable<Comparison> pairs)
    {
        var list = pairs.ToArray();
        File.WriteAllText(Path.Combine(root, "comparisons.json"), JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        var lines = new List<string> { "# Safeguard comparison results", "", "Synthetic effects only. PASS requires the unsafe failure, absence of the secure failure and legitimate positive controls.", "", "| Scenario | Unsafe observation | Secure observation | Result |", "|---|---|---|---|" };
        foreach (var p in list) lines.Add($"| {p.Scenario} | {JsonSerializer.Serialize(p.Unsafe.Effects)} | {JsonSerializer.Serialize(p.Secure.Effects)} | PASS |");
        File.WriteAllLines(Path.Combine(root, "summary.md"), lines);
    }
}
