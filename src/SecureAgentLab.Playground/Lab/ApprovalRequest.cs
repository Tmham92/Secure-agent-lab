namespace SecureAgentLab.Playground.Lab;

public sealed record ApprovalRequest(string RunId, Message Message);

// One run owns its data and atomic authorization state; no user-supplied host paths.

