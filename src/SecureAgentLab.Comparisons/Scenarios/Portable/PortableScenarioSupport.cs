using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Durable.Execution;

namespace SecureAgentLab.Comparisons.Scenarios.Portable;

internal static class PortableScenarioSupport
{
    internal static (ComparisonFixture Unsafe, ComparisonFixture Secure) Fixtures(string root, string id) => (new(root, id, "unsafe"), new(root, id, "secure"));
    internal static Proposal Draft(string content = ComparisonFixture.Reviewed) => new(Operation.PublishReport, "reports/draft", Content: content, ExpectedVersion: 0);
    internal static bool Positive(Gateway gateway, string run) => gateway.Execute(run, new(Operation.ReadDocument, "documents/task")).Result == Gateway.TaskDocument;
    internal static bool Positive(DurableGateway gateway, string run) => gateway.Execute(run, new(Operation.ReadDocument, "documents/task")).Result == Gateway.TaskDocument;
}
