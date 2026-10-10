using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Durable.Models;

public sealed record PreparedChange(DomainState Domain, SignedCheckpoint Previous, AuditEntry Event);
