namespace SecureAgentLab.Core.Contracts;

public sealed record Decision(Outcome Outcome, string Reason, string? Result = null);
