namespace SecureAgentLab.Core.Contracts;

public interface IRunBudgetStore
{
    SessionSnapshot? GetSession(string runId);
}
