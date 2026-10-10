namespace SecureAgentLab.Collaboration.Contracts;

public sealed record MethodEvent(long Sequence, string Run, string Agent, string Action, bool Allowed, string Reason, Envelope? Message = null, string? Answer = null);
