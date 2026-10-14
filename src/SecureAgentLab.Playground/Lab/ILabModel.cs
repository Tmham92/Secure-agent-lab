namespace SecureAgentLab.Playground.Lab;

public interface ILabModel
{
    bool Configured
    {
        get;
    }
    Task<ToolCall[]> ProposeAsync(string document, TraceEntry[] history, CancellationToken cancellationToken);
}

