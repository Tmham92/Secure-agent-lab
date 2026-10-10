using SecureAgentLab.Core.Grants;

namespace SecureAgentLab.Core.Contracts;

public interface IApprovalStore
{
    bool TryGet(string ticket, out ApprovalState? approval);
}
