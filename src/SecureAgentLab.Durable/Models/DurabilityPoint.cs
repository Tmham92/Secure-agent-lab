namespace SecureAgentLab.Durable.Models;

public enum DurabilityPoint
{
    AfterPrepared,
    AfterAuditAcknowledged,
    AfterStateCommitted
}
