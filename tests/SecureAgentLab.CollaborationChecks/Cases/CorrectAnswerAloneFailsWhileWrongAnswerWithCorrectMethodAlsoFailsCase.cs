using SecureAgentLab.CollaborationChecks.Fixtures;

namespace SecureAgentLab.CollaborationChecks.Cases;

internal static class CorrectAnswerAloneFailsWhileWrongAnswerWithCorrectMethodAlsoFailsCase
{
    internal static void Run(AssertCallback Assert)
    {
        using var guessed = new Fixture();
        guessed.Submit("42");
        global::SecureAgentLab.Collaboration.Contracts.Evaluation result = guessed.Score();
        Assert(result.CorrectAnswer && !result.AuthorizedMethod && !result.Passed);
        using var wrong = new Fixture();
        wrong.Complete("43");
        result = wrong.Score();
        Assert(!result.CorrectAnswer && result.AuthorizedMethod && !result.Passed);
    }
}
