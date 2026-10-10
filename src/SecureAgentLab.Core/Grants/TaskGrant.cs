using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Policy;

namespace SecureAgentLab.Core.Grants;

public sealed class TaskGrant
{
    public string PolicyVersion
    {
        get;
    }
    public DateTimeOffset ExpiresAt
    {
        get;
    }
    public int CallLimit
    {
        get;
    }
    public long ResponseByteLimit
    {
        get;
    }
    public ImmutableArray<ResourcePermission> Permissions
    {
        get;
    }
    public string Fingerprint
    {
        get;
    }

    public TaskGrant(string policyVersion, DateTimeOffset expiresAt, int calls, long bytes, IEnumerable<ResourcePermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        Permissions = permissions.ToImmutableArray();
        if (string.IsNullOrWhiteSpace(policyVersion) || expiresAt.Offset != TimeSpan.Zero || calls < 0 || bytes < 0 || Permissions.IsEmpty || Permissions.Any(p => p is null || !DefaultPolicy.IsSupported(p.Operation, p.Resource)) || Permissions.Distinct().Count() != Permissions.Length)
        {
            throw new ArgumentException("Invalid task grant.");
        }

        PolicyVersion = policyVersion;
        ExpiresAt = expiresAt;
        CallLimit = calls;
        ResponseByteLimit = bytes;
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            PolicyVersion,
            ExpiresAt,
            CallLimit,
            ResponseByteLimit,
            Permissions = Permissions.OrderBy(p => p.Operation).ThenBy(p => p.Resource, StringComparer.Ordinal).ToArray()
        })));
    }

    public static TaskGrant Default(DateTimeOffset expiresAt, int calls, long bytes) => new("synthetic-v1", expiresAt, calls, bytes, [new(Operation.ReadDocument, "documents/task"), new(Operation.PublishReport, "reports/draft")]);
}
