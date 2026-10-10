using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Recovery;
namespace SecureAgentLab.DurableChecks.Fixtures;

sealed class SwitchableCollector(IAuditCollector inner) : IAuditCollector
{
    public IAuditCollector Inner { get; set; } = inner;
    public bool Offline
    {
        get; set;
    }
    public bool LoseAcknowledgement
    {
        get; set;
    }

    private void Check()
    {
        if (Offline)
        {
            throw new AuthorizationDependencyException("sink_offline");
        }
    }

    public SignedCheckpoint GetCheckpoint()
    {
        Check();
        return Inner.GetCheckpoint();
    }

    public IReadOnlyList<SignedAuditEvent> GetEvents()
    {
        Check();
        return Inner.GetEvents();
    }

    public SignedCheckpoint Append(AuditEntry e)
    {
        Check();
        var result = Inner.Append(e);
        if (LoseAcknowledgement)
        {
            throw new AuthorizationDependencyException("ack_lost");
        }

        return result;
    }
}
