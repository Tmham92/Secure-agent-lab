namespace SecureAgentLab.Comparisons.Evidence;

public sealed record Comparison(string Scenario, string MissingSafeguard, string Input, string InputSha256, Observation Unsafe, Observation Secure, bool Passed, long ElapsedMilliseconds);
