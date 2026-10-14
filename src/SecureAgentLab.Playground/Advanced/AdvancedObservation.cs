using System.Text.Json;

namespace SecureAgentLab.Playground.Advanced;

public sealed record AdvancedObservation(string Variant, string Decision, Dictionary<string, JsonElement> Effects, bool PositiveControl);
