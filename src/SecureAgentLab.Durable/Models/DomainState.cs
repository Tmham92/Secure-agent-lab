using SecureAgentLab.Durable.Persistence;

namespace SecureAgentLab.Durable.Models;

public sealed class DomainState
{
    public Dictionary<string, StoredSession> Sessions { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, StoredApproval> Approvals { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, PublishedEffect> Publications { get; set; } = new(StringComparer.Ordinal);
    public int Reads
    {
        get; set;
    }
    public bool Stopped
    {
        get; set;
    }

    public string Digest() => DurableFiles.Hash(new { Sessions = Sessions.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), Approvals = Approvals.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), Publications = Publications.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(), Reads, Stopped });
}
