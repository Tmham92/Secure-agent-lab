namespace SecureAgentLab.Collaboration.Contracts;

public sealed record Transcript(string Run, string Challenge, DateTimeOffset SealedAt, MethodEvent[] Events);
