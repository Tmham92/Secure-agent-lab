namespace SecureAgentLab.CollaborationChecks.Fixtures;

sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now += delta;
}
