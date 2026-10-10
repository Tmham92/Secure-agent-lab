namespace SecureAgentLab.Core.Contracts;

public interface IAuditSink
{
    IReadOnlyList<AuditEntry> GetAuditSnapshot();
}
