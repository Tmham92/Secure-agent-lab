namespace SecureAgentLab.Playground.Advanced;

public sealed record AdvancedScenario(string Id, string Title, string Category, string Attack, string Defense, string Expected, string Command, string? Project, string[] Arguments, string? EvidenceCase = null);
