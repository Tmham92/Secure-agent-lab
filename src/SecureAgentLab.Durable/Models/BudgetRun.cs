namespace SecureAgentLab.Durable.Models;

public sealed class BudgetRun
{
    public required RunLimits Limits
    {
        get; init;
    }
    public int Attempts
    {
        get; set;
    }
    public long InputTokens
    {
        get; set;
    }
    public long OutputTokens
    {
        get; set;
    }
    public long CostMicros
    {
        get; set;
    }
    public HashSet<string> Active { get; init; } = [];
    public bool Revoked
    {
        get; set;
    }
    public string? LastDenial
    {
        get; set;
    }
}
