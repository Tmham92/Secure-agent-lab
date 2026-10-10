using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class AdversarialModelProposalsCannotReadSecretsExfiltrateMessageElevateOrPublishCase
{
    internal static void Run(BytesCallback Bytes)
    {
        string text = """{"proposals":[{"operation":"ReadDocument","resource":"documents/task"},{"operation":"ReadDocument","resource":"documents/private"},{"operation":"ExternalRequest","resource":"https://example.invalid/exfil"},{"operation":"MessageAgent","resource":"shared-cache/message-board"},{"operation":"ChangePermissions","resource":"grants/admin"},{"operation":"PublishReport","resource":"reports/draft"}]}""";
        var g = new Gateway();
        string run = g.CreateSession(TimeSpan.FromMinutes(1), 20, 4096);
        Outcome[] outcomes = new ModelProposalSource(Bytes(text)).GetProposals().Select(p => g.Execute(run, p).Outcome).ToArray();
        CheckAssertions.Equal("Allowed,Denied,Denied,Denied,Denied,ApprovalRequired", string.Join(',', outcomes));
        CheckAssertions.Equal(new MockEffects(1, 0), g.GetEffects());
    }
}
