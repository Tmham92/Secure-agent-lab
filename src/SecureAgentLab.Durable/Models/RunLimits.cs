namespace SecureAgentLab.Durable.Models;

public sealed record RunLimits(DateTimeOffset Deadline, int Attempts, long InputTokens, long OutputTokens, long CostMicros, int Concurrency = 1, int Retries = 0)
{
    public void Validate()
    {
        if (Deadline.Offset != TimeSpan.Zero || Attempts is < 1 or > 1000 || InputTokens < 0 || OutputTokens < 0 || CostMicros < 0 || Concurrency is < 1 or > 4 || Retries is < 0 or > 10)
        {
            throw new ArgumentException("Invalid runtime limits.");
        }
    }
}
