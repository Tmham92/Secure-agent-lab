namespace SecureAgentLab.Playground.Advanced;

public sealed record AdvancedResult(string ScenarioId, string Status, string Output, int? ExitCode, long ElapsedMilliseconds, AdvancedComparison? Comparison = null);
