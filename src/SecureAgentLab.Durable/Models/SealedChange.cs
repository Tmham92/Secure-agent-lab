namespace SecureAgentLab.Durable.Models;

public sealed record SealedChange(PreparedChange Change, string AuthenticationCode);
