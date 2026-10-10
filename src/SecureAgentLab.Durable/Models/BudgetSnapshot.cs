namespace SecureAgentLab.Durable.Models;

public sealed record BudgetSnapshot(int Attempts, long InputTokens, long OutputTokens, long CostMicros, int Active, bool Revoked, bool Stopped, string? LastDenial);
