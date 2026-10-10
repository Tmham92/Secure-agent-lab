namespace SecureAgentLab.Core.Contracts;

public sealed record SessionSnapshot(DateTimeOffset ExpiresAt, int RemainingCalls, long RemainingResponseBytes, bool Revoked);
