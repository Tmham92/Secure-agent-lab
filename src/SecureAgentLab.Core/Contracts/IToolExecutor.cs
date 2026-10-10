namespace SecureAgentLab.Core.Contracts;

public interface IToolExecutor
{
    Decision Execute(Proposal proposal, long remainingBytes);
    MockEffects GetEffects();
}
