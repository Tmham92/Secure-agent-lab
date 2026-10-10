using SecureAgentLab.Core.Grants;

namespace SecureAgentLab.Core.Contracts;

public interface IPolicyEvaluator
{
    Decision Evaluate(TaskGrant grant, Proposal? proposal);
}
