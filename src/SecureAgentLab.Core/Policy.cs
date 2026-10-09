using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace SecureAgentLab.Core;

public sealed record ResourcePermission(Operation Operation, string Resource);

public sealed class TaskGrant
{
    public string PolicyVersion { get; }
    public DateTimeOffset ExpiresAt { get; }
    public int CallLimit { get; }
    public long ResponseByteLimit { get; }
    public ImmutableArray<ResourcePermission> Permissions { get; }
    public string Fingerprint { get; }

    public TaskGrant(string policyVersion, DateTimeOffset expiresAt, int calls, long bytes,
        IEnumerable<ResourcePermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        Permissions = permissions.ToImmutableArray();
        if (string.IsNullOrWhiteSpace(policyVersion) || expiresAt.Offset != TimeSpan.Zero ||
            calls < 0 || bytes < 0 || Permissions.IsEmpty ||
            Permissions.Any(p => p is null || !DefaultPolicy.IsSupported(p.Operation, p.Resource)) ||
            Permissions.Distinct().Count() != Permissions.Length)
            throw new ArgumentException("Invalid task grant.");
        PolicyVersion = policyVersion;
        ExpiresAt = expiresAt;
        CallLimit = calls;
        ResponseByteLimit = bytes;
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            PolicyVersion, ExpiresAt, CallLimit, ResponseByteLimit,
            Permissions = Permissions.OrderBy(p => p.Operation).ThenBy(p => p.Resource, StringComparer.Ordinal).ToArray()
        })));
    }

    public static TaskGrant Default(DateTimeOffset expiresAt, int calls, long bytes) =>
        new("synthetic-v1", expiresAt, calls, bytes,
            [new(Operation.ReadDocument, "documents/task"), new(Operation.PublishReport, "reports/draft")]);
}

public interface IProposalSource { IEnumerable<Proposal> GetProposals(); }
public interface IPolicyEvaluator { Decision Evaluate(TaskGrant grant, Proposal? proposal); }
public interface IToolExecutor
{
    Decision Execute(Proposal proposal, long remainingBytes);
    MockEffects GetEffects();
}
public interface IApprovalStore { bool TryGet(string ticket, out ApprovalState? approval); }
public interface IRunBudgetStore { SessionSnapshot? GetSession(string runId); }
public interface IAuditSink { IReadOnlyList<AuditEntry> GetAuditSnapshot(); }
public sealed record ApprovalState(string RunId, Proposal Proposal, DateTimeOffset ExpiresAt, bool Consumed);

public sealed class DefaultPolicy : IPolicyEvaluator
{
    public Decision Evaluate(TaskGrant grant, Proposal? p)
    {
        if (p is null || !Enum.IsDefined(p.Operation) || string.IsNullOrWhiteSpace(p.Resource) || p.EstimatedBytes is < 0 || p.ExpectedVersion is < 0)
            return new(Outcome.Denied, "malformed_proposal");
        if (!IsSupported(p.Operation, p.Resource) || !grant.Permissions.Contains(new(p.Operation, p.Resource)))
            return new(Outcome.Denied, "out_of_scope");
        return new(Outcome.Allowed, "within_grant");
    }

    public static bool IsSupported(Operation operation, string resource) =>
        (operation == Operation.ReadDocument && resource is "documents/task" or "documents/reference") ||
        (operation == Operation.PublishReport && resource == "reports/draft");
}

public sealed class SyntheticExecutor : IToolExecutor
{
    private int reads;
    private int publications;
    public Decision Execute(Proposal proposal, long remainingBytes)
    {
        // Revalidate at the executor: a policy implementation cannot enable a generic tool.
        if (!DefaultPolicy.IsSupported(proposal.Operation, proposal.Resource) || proposal.Resource == "documents/reference" || proposal.ExpectedVersion is not null)
            return new(Outcome.Denied, "executor_out_of_scope");
        var publish = proposal.Operation == Operation.PublishReport;
        var result = publish ? Gateway.PublicationResult : Gateway.TaskDocument;
        if (System.Text.Encoding.UTF8.GetByteCount(result) > remainingBytes)
            return new(Outcome.Denied, "response_budget_exhausted");
        if (publish) Interlocked.Increment(ref publications); else Interlocked.Increment(ref reads);
        return new(Outcome.Allowed, publish ? "publication_approved" : "scoped_read", result);
    }
    public MockEffects GetEffects() => new(Volatile.Read(ref reads), Volatile.Read(ref publications));
}

public sealed class DeterministicProposalSource : IProposalSource
{
    public IEnumerable<Proposal> GetProposals() =>
    [new(Operation.ReadDocument, "documents/task"),
     new(Operation.ExternalRequest, "https://synthetic.invalid"),
     new(Operation.MessageAgent, "shared-cache/message-board"),
     new(Operation.ChangePermissions, "grants/admin"),
     new(Operation.PublishReport, "reports/draft", 0)];
}
