using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Persistence;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Durable.Audit;

public sealed class AuditCollectorStore : IAuditCollector
{
    private readonly string logDirectory;
    private readonly string headPath;
    private readonly string logPath;
    private readonly string streamId;
    private readonly string privateKey;
    private readonly CheckpointVerifier verifier;
    private readonly Action<DurabilityPoint>? crash;
    public string PublicKey
    {
        get;
    }

    public AuditCollectorStore(string logDirectory, string checkpointDirectory, string streamId, string privateKey, Action<DurabilityPoint>? crash = null)
    {
        this.logDirectory = Path.GetFullPath(logDirectory);
        checkpointDirectory = Path.GetFullPath(checkpointDirectory);
        if (string.IsNullOrWhiteSpace(streamId) || this.logDirectory.Equals(checkpointDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Separate log and checkpoint directories and a stream ID are required.");
        }

        this.streamId = streamId;
        this.privateKey = privateKey;
        this.crash = crash;
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        if (rsa.KeySize < 2048)
        {
            throw new ArgumentException("Use RSA with at least 2048 bits.");
        }

        PublicKey = rsa.ExportSubjectPublicKeyInfoPem();
        verifier = new(PublicKey, streamId);
        Directory.CreateDirectory(checkpointDirectory);
        headPath = Path.Combine(checkpointDirectory, "head.json");
        logPath = Path.Combine(this.logDirectory, "audit.jsonl");
        using IDisposable lease = DurableFiles.Lock(this.logDirectory);
        if (!File.Exists(headPath))
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 0)
            {
                throw new AuthorizationDependencyException("checkpoint_missing");
            }

            DurableFiles.Write(headPath, Sign(new(streamId, 0, "", null, DateTimeOffset.MinValue)));
        }

        Load();
    }

    private SignedCheckpoint Sign(Checkpoint checkpoint)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);
        return new(checkpoint, Convert.ToBase64String(rsa.SignData(JsonSerializer.SerializeToUtf8Bytes(checkpoint), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
    }

    private (List<SignedAuditEvent> Events, SignedCheckpoint Head) Load()
    {
        SignedCheckpoint head = DurableFiles.Read<SignedCheckpoint>(headPath);
        verifier.Verify(head);
        var events = new List<SignedAuditEvent>();
        if (File.Exists(logPath))
        {
            byte[] bytes = File.ReadAllBytes(logPath);
            if (bytes.Length > 0 && bytes[^1] != (byte)'\n')
            {
                throw new AuthorizationDependencyException("audit_partial_tail");
            }

            foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                events.Add(JsonSerializer.Deserialize<SignedAuditEvent>(line) ?? throw new AuthorizationDependencyException("audit_invalid"));
            }
        }

        verifier.VerifyEvents(events);
        if (head.Value.Sequence > events.Count || (head.Value.Sequence > 0 && events[(int)head.Value.Sequence - 1].Checkpoint.Value != head.Value))
        {
            throw new AuthorizationDependencyException("audit_truncated_or_rewritten");
        }

        if (head.Value.Sequence == 0 && (head.Value.Hash != "" || head.Value.StateDigest is not null))
        {
            throw new AuthorizationDependencyException("invalid_genesis");
        }

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
        using IDisposable lease = DurableFiles.Lock(logDirectory);
        return Load().Head;
    }

    public IReadOnlyList<SignedAuditEvent> GetEvents()
    {
        using IDisposable lease = DurableFiles.Lock(logDirectory);
        return Load().Events.AsReadOnly();
    }

    public SignedCheckpoint Append(AuditEntry entry)
    {
        using IDisposable lease = DurableFiles.Lock(logDirectory);
        (List<SignedAuditEvent>? events, SignedCheckpoint? head) = Load();
        if (entry.Sequence == head.Value.Sequence && entry.Hash == head.Value.Hash)
        {
            return head;
        }

        if (entry.Sequence != head.Value.Sequence + 1 || entry.PreviousHash != head.Value.Hash || entry.Hash != AuditChain.ComputeHash(entry) || entry.UtcTime < head.Value.UtcTime || entry.StateDigest is null || entry.Proposal?.Content is not null)
        {
            throw new AuthorizationDependencyException("audit_append_rejected");
        }

        SignedCheckpoint checkpoint = Sign(new(streamId, entry.Sequence, entry.Hash, entry.StateDigest, entry.UtcTime));
        using (var file = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            byte[] line = JsonSerializer.SerializeToUtf8Bytes(new SignedAuditEvent(entry, checkpoint));
            file.Write(line);
            file.WriteByte((byte)'\n');
            file.Flush(flushToDisk: true);
        }

        crash?.Invoke(DurabilityPoint.AfterAuditAcknowledged);
        DurableFiles.Write(headPath, checkpoint);
        return checkpoint;
    }
}
