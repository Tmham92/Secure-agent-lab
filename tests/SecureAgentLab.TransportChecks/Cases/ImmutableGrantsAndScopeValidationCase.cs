using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.TransportChecks.Fixtures;
namespace SecureAgentLab.TransportChecks.Cases;

internal static class ImmutableGrantsAndScopeValidationCase
{
    internal static Task Run(global::SecureAgentLab.TransportChecks.Fixtures.TestClock clock, global::SecureAgentLab.Core.Contracts.Proposal publish, global::SecureAgentLab.Core.Contracts.Proposal read)
    {
        ResourcePermission[] permissions = [new(Operation.ReadDocument, "documents/task")];
        var grant = new TaskGrant("v1", clock.GetUtcNow().AddMinutes(2), 3, 4096, permissions);
        permissions[0] = new(Operation.PublishReport, "reports/draft");
        var g = new Gateway(clock);
        string run = g.CreateSession(grant);
        CheckAssertions.Equal(Outcome.Allowed, g.Execute(run, read).Outcome);
        CheckAssertions.Equal(Outcome.Denied, g.Execute(run, publish).Outcome);
        CheckAssertions.Throws<InvalidOperationException>(() => g.ApprovePublication(run, publish, TimeSpan.FromMinutes(1)));
        var changed = new TaskGrant("v2", grant.ExpiresAt, 3, 4096, grant.Permissions);
        CheckAssertions.Assert(changed.Fingerprint != grant.Fingerprint, "Policy version missing from binding");
        CheckAssertions.Throws<ArgumentException>(() => new TaskGrant("v1", grant.ExpiresAt, 3, 4096, [new((Operation)999, "documents/task")]));
        CheckAssertions.Throws<ArgumentException>(() => new TaskGrant("v1", grant.ExpiresAt, 3, 4096, [new(Operation.ExternalRequest, "https://synthetic.invalid")]));
        CheckAssertions.Throws<ArgumentException>(() => new TaskGrant("v1", grant.ExpiresAt, 3, 4096, []));
        var executor = new SyntheticExecutor();
        CheckAssertions.Equal(Outcome.Denied, executor.Execute(new(Operation.ReadDocument, "secrets"), 4096).Outcome);
        CheckAssertions.Equal(new MockEffects(0, 0), executor.GetEffects());
        return Task.CompletedTask;
    }
}
