using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Persistence;

namespace SecureAgentLab.Durable.Approvals;
// Pure approval binding; transaction ownership remains with DurableGateway.
internal static class PublicationBinding
{
    internal static string ContentHash(Proposal proposal) => DurableFiles.Hash(proposal.Content ?? Gateway.DraftReport);
    internal static string Binding(string run, Proposal p, StoredGrant grant)
    {
        string original = DurableFiles.Hash(new
        {
            Run = run,
            p.Operation,
            p.Resource,
            p.EstimatedBytes,
            ContentHash = ContentHash(p),
            grant.PolicyVersion,
            Grant = grant.ToGrant().Fingerprint
        });
        return p.ExpectedVersion is null ? original : DurableFiles.Hash(new
        {
            Original = original,
            p.ExpectedVersion
        });
    }
}
