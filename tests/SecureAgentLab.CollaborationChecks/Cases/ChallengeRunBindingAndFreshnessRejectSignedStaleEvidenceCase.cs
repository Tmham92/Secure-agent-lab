using System.Security.Cryptography;
using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.Collaboration.Evaluation;
using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class ChallengeRunBindingAndFreshnessRejectSignedStaleEvidenceCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        f.Complete();
        f.Evaluator.Challenge();
        var wrong = f.Broker.Seal("demo-run", new string('A', 64));
        Assert(!f.Evaluator.Evaluate(wrong).AcceptedEvidence);
        using var stale = new Fixture();
        stale.Complete();
        var c = stale.Evaluator.Challenge();
        var e = stale.Broker.Seal("demo-run", c.Challenge);
        stale.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert(!stale.Evaluator.Evaluate(e).AcceptedEvidence);
        using var different = new Fixture();
        different.Complete();
        using var verifier = RSA.Create();
        verifier.ImportFromPem(different.Signing.ExportSubjectPublicKeyInfoPem());
        var evaluator = new IndependentEvaluator(verifier, "other-run", "researcher", "writer", "42", different.Clock);
        EvaluationChallenge d = evaluator.Challenge();
        var evidence = different.Broker.Seal("demo-run", d.Challenge);
        Assert(!evaluator.Evaluate(evidence).AcceptedEvidence); // Right challenge/key/time; wrong run only.
    }
}
