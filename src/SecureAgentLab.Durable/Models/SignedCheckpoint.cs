namespace SecureAgentLab.Durable.Models;

public sealed record SignedCheckpoint(Checkpoint Value, string Signature);
