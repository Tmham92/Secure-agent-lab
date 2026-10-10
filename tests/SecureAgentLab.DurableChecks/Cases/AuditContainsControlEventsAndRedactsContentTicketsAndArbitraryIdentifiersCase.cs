using SecureAgentLab.Core.Audit;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class AuditContainsControlEventsAndRedactsContentTicketsAndArbitraryIdentifiersCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal draft, NewFixtureCallback NewFixture)
    {
        Fixture f = NewFixture();
        DurableGateway g = f.Open();
        string run = f.Run(g);
        Proposal secretDraft = draft with
        {
            Content = "synthetic-secret-marker"
        };
        string ticket = g.ApprovePublication(run, secretDraft, TimeSpan.FromMinutes(1));
        g.Execute(run, new(Operation.ExternalRequest, "https://secret-marker.invalid/credential"));
        g.Execute(run, secretDraft, ticket, "idempotency-secret-marker");
        g.Revoke(run);
        g.Stop();
        IReadOnlyList<AuditEntry> events = g.GetAuditSnapshot();
        foreach (string? type in new[]
        {
            "grant_issued",
            "approval_issued",
            "execution",
            "run_revoked",
            "gateway_stopped"
        }

        )
        {
            CheckAssertions.Assert(events.Any(e => e.EventType == type), "Control event missing: " + type);
        }

        string auditText = File.ReadAllText(Path.Combine(f.Log, "audit.jsonl"));
        string stateText = File.ReadAllText(Path.Combine(f.State, "state.json"));
        foreach (string? secret in new[]
        {
            secretDraft.Content!,
            ticket,
            "idempotency-secret-marker",
            "https://secret-marker.invalid/credential"
        }

        )
        {
            CheckAssertions.Assert(!auditText.Contains(secret) && !stateText.Contains(secret), "Sensitive input persisted");
        }
        CheckAssertions.
                Assert(AuditChain.VerifyAudit(events), "Audit invalid");
    }
}
