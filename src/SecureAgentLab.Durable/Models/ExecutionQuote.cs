namespace SecureAgentLab.Durable.Models;

public sealed record ExecutionQuote(long InputTokens, long OutputTokens, long CostMicros, int RetryOrdinal = 0);
