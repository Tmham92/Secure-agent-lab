using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class ExpiryRevocationAndStopPreventLaterEffectsCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        f.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert(!f.Send("researcher", "writer", "facts", "x").Allowed);
        using var r = new Fixture();
        r.Broker.Revoke("researcher");
        Assert(!r.Send("researcher", "writer", "facts", "x").Allowed);
        using var s = new Fixture();
        s.Broker.Stop();
        Assert(!s.Send("researcher", "writer", "facts", "x").Allowed);
        try
        {
            s.Register("late", "demo-run", []);
            throw new Exception("Stop permitted a new grant");
        }
        catch (InvalidOperationException)
        {
        }
        Assert(f.Broker.Delivered + r.Broker.Delivered + s.Broker.Delivered == 0);
    }
}
