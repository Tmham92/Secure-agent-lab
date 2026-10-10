using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.Durable.Persistence;
using SecureAgentLab.Durable.Recovery;
using SecureAgentLab.DurableChecks.Fixtures;

namespace SecureAgentLab.DurableChecks.Cases;

internal static class StateAlterationStateRollbackAndMissingStateFailClosedCase
{
    internal static void Run(global::SecureAgentLab.Core.Contracts.Proposal read, NewFixtureCallback NewFixture)
    {
        foreach (string? mode in new[]
        {
            "tamper",
            "rollback",
            "delete"
        }

        )
        {
            Fixture f = NewFixture();
            DurableGateway g = f.Open();
            string run = f.Run(g);
            string path = Path.Combine(f.State, "state.json");
            byte[] previous = File.ReadAllBytes(path);
            g.Execute(run, read);
            if (mode == "delete")
            {
                File.Delete(path);
            }
            else if (mode == "rollback")
            {
                File.WriteAllBytes(path, previous);
            }
            else
            {
                DurableSnapshot state = DurableFiles.Read<DurableSnapshot>(path);
                state.Domain.Reads++;
                DurableFiles.Write(path, state);
            }
            CheckAssertions.
                        Throws<AuthorizationDependencyException>(() => f.Open());
        }
    }
}
