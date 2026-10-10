namespace SecureAgentLab.Collaboration.Grants;

public sealed record AgentGrant(string Run, string Agent, DateTimeOffset ExpiresAt, int Calls, int Bytes, global::SecureAgentLab.Collaboration.Grants.Route[] Routes);
