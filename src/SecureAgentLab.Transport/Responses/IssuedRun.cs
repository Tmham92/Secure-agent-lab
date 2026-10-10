namespace SecureAgentLab.Transport.Responses;

public sealed record IssuedRun(string RunId, string WorkerCredential, string GrantFingerprint, DateTimeOffset ExpiresAt);
