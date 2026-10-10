namespace SecureAgentLab.Durable.Models;

public sealed record Checkpoint(string StreamId, long Sequence, string Hash, string? StateDigest, DateTimeOffset UtcTime, int Version = 1);
