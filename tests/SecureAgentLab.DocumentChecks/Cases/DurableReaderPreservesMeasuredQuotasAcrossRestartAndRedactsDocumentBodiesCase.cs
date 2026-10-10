using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.DocumentChecks.Fixtures;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;

namespace SecureAgentLab.DocumentChecks.Cases;

internal static class DurableReaderPreservesMeasuredQuotasAcrossRestartAndRedactsDocumentBodiesCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, FixtureCallback Fixture, ReaderCallback Reader)
    {
        string root = Fixture();
        using var rsa = RSA.Create(2048);
        string stream = Guid.NewGuid().ToString("N");
        var sink = new AuditCollectorStore(Path.Combine(root, "audit"), Path.Combine(root, "head"), stream, rsa.ExportPkcs8PrivateKeyPem());
        byte[] key = RandomNumberGenerator.GetBytes(32);
        string state = Path.Combine(root, "state");
        DurableGateway Open() => new(state, sink, rsa.ExportSubjectPublicKeyInfoPem(), stream, key, documents: Reader(root));
        DurableGateway g = Open();
        int bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
        string run = g.CreateSession(TaskGrant.Default(DateTimeOffset.UtcNow.AddMinutes(3), 20, bytes));
        CheckAssertions.Equal(Gateway.TaskDocument, g.Execute(run, read).Result);
        g = Open();
        CheckAssertions.Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        CheckAssertions.Equal("response_budget_exhausted", g.Execute(run, read).Reason);
        CheckAssertions.Equal(new MockEffects(1, 0), g.GetEffects());
        IReadOnlyList<SignedAuditEvent> events = sink.GetEvents();
        CheckAssertions.Equal(SyntheticDocuments.Catalog[0].Sha256, events.Single(e => e.Entry.Reason == "scoped_read").Entry.ContentHash);
        string json = JsonSerializer.Serialize(events);
        CheckAssertions.Assert(!json.Contains("Café") && !json.Contains(root), "Document body or path leaked into audit");
    }
}
