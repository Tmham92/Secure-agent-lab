using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class GrantCopiesPreventCallerMutationAndBindingForgeryCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        global::SecureAgentLab.Collaboration.Grants.Route[] routes = [new("extra", "writer", "facts")];
        var token = f.Broker.Register(new("demo-run", "extra", f.Clock.GetUtcNow().AddMinutes(2), 3, 512, routes));
        routes[0] = new("extra", "writer", "supervisor");
        var caller = f.Credentials.Validate(token)!;
        Assert(!f.Broker.Execute(caller, "send", new("writer", "supervisor", "elevate")).Allowed);
        Assert(!f.Broker.Execute(caller with
        {
            GrantFingerprint = "forged"
        }, "send", new("writer", "facts", "20")).Allowed);
        Assert(f.Broker.Delivered == 0);
    }
}
