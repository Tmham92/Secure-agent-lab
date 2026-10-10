using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Recovery;

namespace SecureAgentLab.Comparisons.Fixtures;

public sealed class SwitchableSink(IAuditCollector inner) : IAuditCollector
{
    public bool LoseAck
    {
        get; set;
    }
    public bool Offline
    {
        get; set;
    }

    private void Ready()
    {
        if (Offline)
        {
            throw new AuthorizationDependencyException("synthetic_sink_outage");
        }
    }

    public SignedCheckpoint GetCheckpoint()
    {
        Ready();
        return inner.GetCheckpoint();
    }

    public IReadOnlyList<SignedAuditEvent> GetEvents()
    {
        Ready();
        return inner.GetEvents();
    }

    public SignedCheckpoint Append(AuditEntry e)
    {
        Ready();
        SignedCheckpoint result = inner.Append(e);
        if (LoseAck)
        {
            throw new AuthorizationDependencyException("synthetic_lost_ack");
        }

        return result;
    }
}
