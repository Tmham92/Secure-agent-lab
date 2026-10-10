using System.Security.Cryptography;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.ModelChecks.Fixtures;
namespace SecureAgentLab.ModelChecks.Cases;

internal static class DurableGatewayKeepsModelPublicationsUnapprovedAndRecordsActualEffectsCase
{
    internal static void Run()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "SecureAgentLab.slnx")))
        {
            repo = repo.Parent;
        }

        string folder = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.FixturePath(global::SecureAgentLab.Core.Diagnostics.ArtifactRun.PathForAssembly("model-checks"));
        using var rsa = RSA.Create(2048);
        string stream = Guid.NewGuid().ToString("N");
        var sink = new AuditCollectorStore(Path.Combine(folder, "log"), Path.Combine(folder, "head"), stream, rsa.ExportPkcs8PrivateKeyPem());
        var g = new DurableGateway(Path.Combine(folder, "state"), sink, rsa.ExportSubjectPublicKeyInfoPem(), stream, RandomNumberGenerator.GetBytes(32));
        string run = g.CreateSession(TaskGrant.Default(DateTimeOffset.UtcNow.AddMinutes(2), 20, 4096));
        var source = ModelProposalSource.FromFile(Path.Combine(global::SecureAgentLab.Core.Diagnostics.ArtifactRun.FindRepository(), "fixtures", "model", "adversarial-proposals.json"));
        IEnumerable<Outcome> outcomes = source.GetProposals().Select(p => g.Execute(run, p).Outcome);
        CheckAssertions.Equal("Allowed,Denied,Denied,Denied,Denied,ApprovalRequired", string.Join(',', outcomes));
        CheckAssertions.Equal(new MockEffects(1, 0), g.GetEffects());
        CheckAssertions.Equal(14, g.GetSession(run)!.RemainingCalls);
        CheckAssertions.Equal(7, sink.GetEvents().Count);
    }
}

