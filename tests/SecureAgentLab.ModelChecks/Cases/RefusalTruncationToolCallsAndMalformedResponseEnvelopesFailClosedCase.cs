using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;
using SecureAgentLab.ModelHost.Clients;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class RefusalTruncationToolCallsAndMalformedResponseEnvelopesFailClosedCase
{
    internal static void Run(string Valid, BytesCallback Bytes, EnvelopeCallback Envelope)
    {
        foreach (string? text in new[]
        {
            Envelope(Valid, "incomplete"),
            Envelope("I refuse"),
            Envelope(Valid).Replace("output_text", "refusal"),
            Envelope(Valid).Replace("\"message\"", "\"function_call\""),
            "{}",
            Envelope(Valid).Replace("\"status\":\"completed\",", "\"status\":\"completed\",\"status\":\"completed\",")
        }

        )
        {
            CheckAssertions.Throws<ModelOutputException>(() => ResponsesProposalClient.Extract(Bytes(text)));
        }
    }
}
