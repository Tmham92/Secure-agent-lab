namespace SecureAgentLab.Transport.Credentials;

public sealed record Credential(string Issuer, string Audience, string Subject, string Role, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, string? GrantFingerprint);
