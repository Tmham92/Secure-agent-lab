using System.Text.Json.Serialization;

namespace SecureAgentLab.Playground.Lab;

public sealed record ToolCall(
    [property: JsonRequired] string Tool,
    [property: JsonRequired] string Path = "",
    [property: JsonRequired] string Recipient = "",
    [property: JsonRequired] string Text = "",
    [property: JsonRequired] bool Approved = false);
