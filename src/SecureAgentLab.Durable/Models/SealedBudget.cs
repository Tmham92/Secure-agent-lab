namespace SecureAgentLab.Durable.Models;

public sealed record SealedBudget(BudgetState State, string AuthenticationCode);
