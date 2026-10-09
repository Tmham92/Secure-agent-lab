using System.Security.Cryptography;
using System.Text.Json;

namespace SecureAgentLab.Core;

public static class AuditChain
{
    public const string Genesis = "";

    // Positional record fields give a fixed serialization order. The hash itself
    // is excluded; null fields and numeric enums are included without ambiguity.
    public static string ComputeHash(AuditEntry entry) => Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entry with { Hash = "" })));

    public static bool VerifyAudit(IEnumerable<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        long sequence = 1;
        var previous = Genesis;
        foreach (var entry in entries)
        {
            if (entry is null || entry.Sequence != sequence || entry.PreviousHash != previous ||
                entry.Hash != ComputeHash(entry))
                return false;
            previous = entry.Hash;
            sequence++;
        }
        // A valid prefix (including empty) cannot prove completeness.
        return true;
    }

}
