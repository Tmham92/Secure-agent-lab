using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
namespace SecureAgentLab.Checks.Fixtures;

internal static class CheckAssertions
{
    internal static Proposal Read(string resource = "documents/task", long? estimate = null) => new(Operation.ReadDocument, resource, estimate);
    internal static Proposal Publish(string resource = "reports/draft", long? estimate = 0) => new(Operation.PublishReport, resource, estimate);
    internal static string Session(Gateway g, int calls = 100, long bytes = 100_000) => g.CreateSession(TimeSpan.FromMinutes(5), calls, bytes);
    internal static string Approve(Gateway g, string run) => g.ApprovePublication(run, Publish(), TimeSpan.FromMinutes(1));
    internal static void Expect(Decision d, Outcome outcome, string reason)
    {
        Equal(outcome, d.Outcome);
        Equal(reason, d.Reason);
        if (outcome != Outcome.Allowed)
        {
            Equal<string?>(null, d.Result);
        }
    }

    internal static void Effects(Gateway g, int reads, int publications) => Equal(new MockEffects(reads, publications), g.GetEffects());
    internal static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
    internal static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}");
    }
}
