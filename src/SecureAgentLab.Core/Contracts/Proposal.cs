namespace SecureAgentLab.Core.Contracts;

public sealed record Proposal(Operation Operation, string Resource, long? EstimatedBytes = null, string? Content = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? ExpectedVersion = null);
