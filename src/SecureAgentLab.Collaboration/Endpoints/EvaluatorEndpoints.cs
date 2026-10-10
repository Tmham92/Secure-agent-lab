using SecureAgentLab.Collaboration.Contracts;
using SecureAgentLab.Collaboration.Evaluation;

namespace SecureAgentLab.Collaboration.Endpoints;

internal static class EvaluatorEndpoints
{
    internal static void Map(WebApplication app, IndependentEvaluator evaluator)
    {
        app.MapPost("/operator/challenge", () => evaluator!.Challenge());
        app.MapPost("/operator/evaluate", (SignedTranscript request) => evaluator!.Evaluate(request));
    }
}
