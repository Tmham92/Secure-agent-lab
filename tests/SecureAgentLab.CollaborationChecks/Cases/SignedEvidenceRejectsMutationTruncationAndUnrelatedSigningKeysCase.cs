using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class SignedEvidenceRejectsMutationTruncationAndUnrelatedSigningKeysCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        f.Complete();
        var c = f.Evaluator.Challenge();
        var e = f.Broker.Seal("demo-run", c.Challenge);
        Transcript t = JsonSerializer.Deserialize<Transcript>(Convert.FromBase64String(e.Payload))!;
        foreach (Transcript? changed in new[]
        {
            t with
            {
                Events = t.Events[..^1]
            },
            t with
            {
                Run = "other-run"
            },
            t with
            {
                Events = t.Events.Select(x => x.Action == "submit" ? x with { Answer = "full marks" } : x).ToArray()
            }
        }

        )
        {
            Assert(!f.Evaluator.Evaluate(e with
            {
                Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(changed))
            }).AcceptedEvidence);
        }

        using var other = RSA.Create(2048);
        Assert(!f.Evaluator.Evaluate(e with
        {
            Signature = Convert.ToBase64String(other.SignData(Convert.FromBase64String(e.Payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        }).AcceptedEvidence);
        Assert(f.Evaluator.Evaluate(e).Passed, "Invalid evidence consumed the valid challenge");
    }
}
