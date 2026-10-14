namespace SecureAgentLab.Playground.Lab;

public sealed record ToolDecision(bool Allowed, string Reason, string? Result = null);

