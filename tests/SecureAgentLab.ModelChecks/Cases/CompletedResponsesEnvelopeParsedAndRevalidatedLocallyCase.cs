using System.Text;
using SecureAgentLab.ModelChecks.Fixtures;
using SecureAgentLab.ModelHost.Clients;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class CompletedResponsesEnvelopeParsedAndRevalidatedLocallyCase
{
    internal static void Run(string Valid, BytesCallback Bytes, EnvelopeCallback Envelope)
    {
        CheckAssertions.Equal(Valid, Encoding.UTF8.GetString(ResponsesProposalClient.Extract(Bytes(Envelope(Valid)))));
    }
}
