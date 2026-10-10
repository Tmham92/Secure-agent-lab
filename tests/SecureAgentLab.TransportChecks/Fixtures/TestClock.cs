namespace SecureAgentLab.TransportChecks.Fixtures;

sealed class TestClock(DateTimeOffset? initial = null) : TimeProvider
{
    private DateTimeOffset _now = initial ?? new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan elapsed) => _now = _now.Add(elapsed);
}
