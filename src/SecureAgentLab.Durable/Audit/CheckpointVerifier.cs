using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Core.Audit;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Durable.Audit;

public sealed class CheckpointVerifier(string publicKey, string streamId)
{
    public void Verify(SignedCheckpoint checkpoint)
    {
        if (checkpoint is null || checkpoint.Value is null || checkpoint.Signature is null || checkpoint.Value.Version != 1 || publicKey.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            throw new AuthorizationDependencyException("checkpoint_invalid");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        try
        {
            if (rsa.KeySize < 2048 || checkpoint.Value.StreamId != streamId || checkpoint.Value.Sequence < 0 || !rsa.VerifyData(JsonSerializer.SerializeToUtf8Bytes(checkpoint.Value), Convert.FromBase64String(checkpoint.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            {
                throw new AuthorizationDependencyException("checkpoint_invalid");
            }
        }
        catch (FormatException e)
        {
            throw new AuthorizationDependencyException("checkpoint_invalid", e);
        }
    }

    public void VerifyEvents(IReadOnlyList<SignedAuditEvent> events)
    {
        if (!AuditChain.VerifyAudit(events.Select(e => e.Entry)))
        {
            throw new AuthorizationDependencyException("audit_chain_invalid");
        }

        foreach (SignedAuditEvent e in events)
        {
            Verify(e.Checkpoint);
            if (e.Entry.Sequence != e.Checkpoint.Value.Sequence || e.Entry.Hash != e.Checkpoint.Value.Hash || e.Entry.StateDigest != e.Checkpoint.Value.StateDigest || e.Entry.UtcTime != e.Checkpoint.Value.UtcTime)
            {
                throw new AuthorizationDependencyException("audit_checkpoint_mismatch");
            }
        }
    }
}
