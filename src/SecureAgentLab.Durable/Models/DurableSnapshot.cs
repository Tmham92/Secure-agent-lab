namespace SecureAgentLab.Durable.Models;

public sealed record DurableSnapshot(DomainState Domain, SignedCheckpoint Checkpoint);
