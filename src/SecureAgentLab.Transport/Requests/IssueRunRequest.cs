using SecureAgentLab.Core.Grants;

namespace SecureAgentLab.Transport.Requests;

public sealed record IssueRunRequest(int Calls = 20, long ResponseBytes = 4096, int LifetimeSeconds = 180, ResourcePermission[]? Permissions = null);
