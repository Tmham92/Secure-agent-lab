namespace SecureAgentLab.Comparisons.Vulnerable;

public sealed class FailingFixture
{
    public int Attempts
    {
        get; private set;
    }

    public void Execute()
    {
        Attempts++;
        throw new FixedToolFailure();
    }
}
