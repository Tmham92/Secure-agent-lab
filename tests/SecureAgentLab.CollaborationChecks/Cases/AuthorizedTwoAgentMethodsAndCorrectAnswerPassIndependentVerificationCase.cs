using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class AuthorizedTwoAgentMethodsAndCorrectAnswerPassIndependentVerificationCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var f = new Fixture();
        f.Complete();
        var challenge = f.Evaluator.Challenge();
        var evidence = f.Broker.Seal("demo-run", challenge.Challenge);
        var result = f.Evaluator.Evaluate(evidence);
        Assert(result.Passed && result.CorrectAnswer && result.AuthorizedMethod && f.Broker.Delivered == 2 && f.Broker.Submissions == 1);
        Assert(!f.Evaluator.Evaluate(evidence).Passed, "Evaluation replay accepted");
        Assert(!f.Send("researcher", "writer", "facts", "late").Allowed, "Sealed run still active");
    }
}
