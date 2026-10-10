namespace SecureAgentLab.Comparisons.Vulnerable;

public sealed class MissingVersionStore
{
    private readonly HashSet<string> _approved = new(StringComparer.Ordinal);
    public string? Content
    {
        get; private set;
    }
    public int Version
    {
        get; private set;
    }

    public void Approve(string content, long expectedVersion) => _approved.Add(SecureAgentLab.Durable.Persistence.DurableFiles.Hash(new { content, expectedVersion }));
    public void Write(string content, long expectedVersion)
    {
        // Exact reviewed content/version binding retained; comparison deliberately omits checking CURRENT version.
        if (!_approved.Remove(SecureAgentLab.Durable.Persistence.DurableFiles.Hash(new
        {
            content,
            expectedVersion
        })))
        {
            throw new InvalidOperationException("Approval binding mismatch");
        }

        Content = content;
        Version++;
    }
}
