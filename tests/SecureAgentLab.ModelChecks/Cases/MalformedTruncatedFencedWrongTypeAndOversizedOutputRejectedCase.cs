using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class MalformedTruncatedFencedWrongTypeAndOversizedOutputRejectedCase
{
    internal static void Run(string Valid, RejectCallback Reject)
    {
        foreach (string? text in new[]
        {
            "null",
            "[]",
            "{",
            Valid[..^1],
            "```json\n" + Valid + "\n```",
            Valid + " trailing",
            Valid.Replace("\"documents/task\"", "null"),
            Valid.Replace("documents/task", "\\u0000")
        }

        )
        {
            Reject(text);
        }
        CheckAssertions.
                Throws<ModelOutputException>(() => new ModelProposalSource(new byte[ModelProposalSource.MaximumBytes + 1]));
    }
}
