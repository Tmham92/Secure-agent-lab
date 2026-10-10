namespace SecureAgentLab.ModelChecks.Fixtures;

sealed class ModelClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;
}
