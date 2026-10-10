namespace SecureAgentLab.Durable.Models;

public sealed class BudgetState
{
    public Dictionary<string, BudgetRun> Runs { get; init; } = new(StringComparer.Ordinal);
    public bool Stopped
    {
        get; set;
    }
    public DateTimeOffset Updated
    {
        get; set;
    }
}
