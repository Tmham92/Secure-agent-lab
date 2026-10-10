using System.Text.Json.Serialization;

namespace SecureAgentLab.Durable.Models;

public sealed record PublishedEffect(string Binding, string ApprovalReference, string ContentHash, string Destination, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReportContent = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ReportVersion = 0);
