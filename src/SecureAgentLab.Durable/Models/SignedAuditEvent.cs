using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Durable.Models;

public sealed record SignedAuditEvent(AuditEntry Entry, SignedCheckpoint Checkpoint);
