using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Models;

namespace SecureAgentLab.Durable.Audit;

public interface IAuditCollector
{
    SignedCheckpoint GetCheckpoint();
    SignedCheckpoint Append(AuditEntry entry);
    IReadOnlyList<SignedAuditEvent> GetEvents();
}
