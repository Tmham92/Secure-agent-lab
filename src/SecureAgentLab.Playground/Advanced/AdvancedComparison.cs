namespace SecureAgentLab.Playground.Advanced;

public sealed record AdvancedComparison(string Scenario, string MissingSafeguard, string Input, string InputSha256, AdvancedObservation Unsafe, AdvancedObservation Secure, bool Passed, long ElapsedMilliseconds);
