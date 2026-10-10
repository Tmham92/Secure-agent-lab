using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Grants;

namespace SecureAgentLab.Core.Policy;

public sealed class DefaultPolicy : IPolicyEvaluator
{
    public Decision Evaluate(TaskGrant grant, Proposal? p)
    {
        if (p is null || !Enum.IsDefined(p.Operation) || string.IsNullOrWhiteSpace(p.Resource) || p.EstimatedBytes is < 0 || p.ExpectedVersion is < 0)
        {
            return new(Outcome.Denied, "malformed_proposal");
        }

        if (!IsSupported(p.Operation, p.Resource) || !grant.Permissions.Contains(new(p.Operation, p.Resource)))
        {
            return new(Outcome.Denied, "out_of_scope");
        }

        return new(Outcome.Allowed, "within_grant");
    }

    public static bool IsSupported(Operation operation, string resource) => (operation == Operation.ReadDocument && resource is "documents/task" or "documents/reference") || (operation == Operation.PublishReport && resource == "reports/draft");
}
