using SecureAgentLab.Core.Grants;

namespace SecureAgentLab.Durable.Models;

public sealed record StoredGrant(string PolicyVersion, DateTimeOffset ExpiresAt, int Calls, long Bytes, ResourcePermission[] Permissions)
{
    public TaskGrant ToGrant() => new(PolicyVersion, ExpiresAt, Calls, Bytes, Permissions);
    public static StoredGrant From(TaskGrant grant) => new(grant.PolicyVersion, grant.ExpiresAt, grant.CallLimit, grant.ResponseByteLimit, grant.Permissions.ToArray());
}
