using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureAgentLab.Core;

namespace SecureAgentLab.Durable;

public sealed class AuthorizationDependencyException(string reason, Exception? inner = null) : Exception(reason, inner);
public enum DurabilityPoint { AfterPrepared, AfterAuditAcknowledged, AfterStateCommitted }
public sealed record Checkpoint(string StreamId, long Sequence, string Hash, string? StateDigest, DateTimeOffset UtcTime, int Version = 1);
public sealed record SignedCheckpoint(Checkpoint Value, string Signature);
public sealed record SignedAuditEvent(AuditEntry Entry, SignedCheckpoint Checkpoint);
public interface IAuditCollector
{
    SignedCheckpoint GetCheckpoint();
    SignedCheckpoint Append(AuditEntry entry);
    IReadOnlyList<SignedAuditEvent> GetEvents();
}

public static class DurableFiles
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    public static IDisposable Lock(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var gate = Gates.GetOrAdd(directory, _ => new object());
        Monitor.Enter(gate);
        try
        {
            var until = Environment.TickCount64 + 5000;
            while (true)
            {
                try { return new Lease(gate, new FileStream(Path.Combine(directory, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
                catch (IOException) when (Environment.TickCount64 < until) { Thread.Sleep(5); }
            }
        }
        catch { Monitor.Exit(gate); throw; }
    }
    public static void Write<T>(string path, T value)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path))
        ?? throw new AuthorizationDependencyException("invalid_storage");
    public static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    public static string Hash<T>(T input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
    private sealed class Lease(object gate, FileStream stream) : IDisposable
    {
        public void Dispose() { stream.Dispose(); Monitor.Exit(gate); }
    }
}

public sealed class CheckpointVerifier(string publicKey, string streamId)
{
    public void Verify(SignedCheckpoint checkpoint)
    {
        if (checkpoint is null || checkpoint.Value is null || checkpoint.Signature is null || checkpoint.Value.Version != 1 ||
            publicKey.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new AuthorizationDependencyException("checkpoint_invalid");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        try
        {
            if (rsa.KeySize < 2048 || checkpoint.Value.StreamId != streamId || checkpoint.Value.Sequence < 0 ||
                !rsa.VerifyData(JsonSerializer.SerializeToUtf8Bytes(checkpoint.Value), Convert.FromBase64String(checkpoint.Signature),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new AuthorizationDependencyException("checkpoint_invalid");
        }
        catch (FormatException e) { throw new AuthorizationDependencyException("checkpoint_invalid", e); }
    }
    public void VerifyEvents(IReadOnlyList<SignedAuditEvent> events)
    {
        if (!AuditChain.VerifyAudit(events.Select(e => e.Entry))) throw new AuthorizationDependencyException("audit_chain_invalid");
        foreach (var e in events)
        {
            Verify(e.Checkpoint);
            if (e.Entry.Sequence != e.Checkpoint.Value.Sequence || e.Entry.Hash != e.Checkpoint.Value.Hash ||
                e.Entry.StateDigest != e.Checkpoint.Value.StateDigest || e.Entry.UtcTime != e.Checkpoint.Value.UtcTime)
                throw new AuthorizationDependencyException("audit_checkpoint_mismatch");
        }
    }
}

public sealed class AuditCollectorStore : IAuditCollector
{
    private readonly string logDirectory;
    private readonly string headPath;
    private readonly string logPath;
    private readonly string streamId;
    private readonly string privateKey;
    private readonly CheckpointVerifier verifier;
    private readonly Action<DurabilityPoint>? crash;
    public string PublicKey { get; }
    public AuditCollectorStore(string logDirectory, string checkpointDirectory, string streamId, string privateKey,
        Action<DurabilityPoint>? crash = null)
    {
        this.logDirectory = Path.GetFullPath(logDirectory);
        checkpointDirectory = Path.GetFullPath(checkpointDirectory);
        if (string.IsNullOrWhiteSpace(streamId) || this.logDirectory.Equals(checkpointDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Separate log and checkpoint directories and a stream ID are required.");
        this.streamId = streamId;
        this.privateKey = privateKey;
        this.crash = crash;
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        if (rsa.KeySize < 2048) throw new ArgumentException("Use RSA with at least 2048 bits.");
        PublicKey = rsa.ExportSubjectPublicKeyInfoPem();
        verifier = new(PublicKey, streamId);
        Directory.CreateDirectory(checkpointDirectory);
        headPath = Path.Combine(checkpointDirectory, "head.json");
        logPath = Path.Combine(this.logDirectory, "audit.jsonl");
        using var lease = DurableFiles.Lock(this.logDirectory);
        if (!File.Exists(headPath))
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 0) throw new AuthorizationDependencyException("checkpoint_missing");
            DurableFiles.Write(headPath, Sign(new(streamId, 0, "", null, DateTimeOffset.MinValue)));
        }
        Load();
    }
    private SignedCheckpoint Sign(Checkpoint checkpoint)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        return new(checkpoint, Convert.ToBase64String(rsa.SignData(JsonSerializer.SerializeToUtf8Bytes(checkpoint),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
    }
    private (List<SignedAuditEvent> Events, SignedCheckpoint Head) Load()
    {
        var head = DurableFiles.Read<SignedCheckpoint>(headPath);
        verifier.Verify(head);
        var events = new List<SignedAuditEvent>();
        if (File.Exists(logPath))
        {
            var bytes = File.ReadAllBytes(logPath);
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n') throw new AuthorizationDependencyException("audit_partial_tail");
            foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                events.Add(JsonSerializer.Deserialize<SignedAuditEvent>(line) ?? throw new AuthorizationDependencyException("audit_invalid"));
        }
        verifier.VerifyEvents(events);
        if (head.Value.Sequence > events.Count || (head.Value.Sequence > 0 &&
            events[(int)head.Value.Sequence - 1].Checkpoint.Value != head.Value))
            throw new AuthorizationDependencyException("audit_truncated_or_rewritten");
        if (head.Value.Sequence == 0 && (head.Value.Hash != "" || head.Value.StateDigest is not null))
            throw new AuthorizationDependencyException("invalid_genesis");
        if (events.Count > head.Value.Sequence)
        {
            // A complete signed suffix survived a crash before the independent head was updated.
            head = events[^1].Checkpoint;
            DurableFiles.Write(headPath, head);
        }
        return (events, head);
    }
    public SignedCheckpoint GetCheckpoint()
    {
        using var lease = DurableFiles.Lock(logDirectory);
        return Load().Head;
    }
    public IReadOnlyList<SignedAuditEvent> GetEvents()
    {
        using var lease = DurableFiles.Lock(logDirectory);
        return Load().Events.AsReadOnly();
    }
    public SignedCheckpoint Append(AuditEntry entry)
    {
        using var lease = DurableFiles.Lock(logDirectory);
        var (events, head) = Load();
        if (entry.Sequence == head.Value.Sequence && entry.Hash == head.Value.Hash) return head;
        if (entry.Sequence != head.Value.Sequence + 1 || entry.PreviousHash != head.Value.Hash ||
            entry.Hash != AuditChain.ComputeHash(entry) || entry.UtcTime < head.Value.UtcTime ||
            entry.StateDigest is null || entry.Proposal?.Content is not null)
            throw new AuthorizationDependencyException("audit_append_rejected");
        var checkpoint = Sign(new(streamId, entry.Sequence, entry.Hash, entry.StateDigest, entry.UtcTime));
        using (var file = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            var line = JsonSerializer.SerializeToUtf8Bytes(new SignedAuditEvent(entry, checkpoint));
            file.Write(line);
            file.WriteByte((byte)'\n');
            file.Flush(flushToDisk: true);
        }
        crash?.Invoke(DurabilityPoint.AfterAuditAcknowledged);
        DurableFiles.Write(headPath, checkpoint);
        return checkpoint;
    }
}
