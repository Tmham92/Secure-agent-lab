namespace SecureAgentLab.Durable.Models;

public sealed record StoredApproval(string RunId, string Binding, string ContentHash, string PolicyVersion, DateTimeOffset ExpiresAt, bool Consumed = false);
