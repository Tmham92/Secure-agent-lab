namespace SecureAgentLab.Comparisons.Evidence;

public sealed record Observation(string Variant, string Decision, Dictionary<string, object> Effects, bool PositiveControl);
