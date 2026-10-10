using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class HTTPMalformedAndUnknownOperationsExecuteNoToolCase
{
    internal static async Task RunAsync(global::SecureAgentLab.Core.Execution.Gateway gateway, ExecuteCallback Execute, IssueCallback Issue)
    {
        IssuedRun run = await Issue();
        MockEffects before = gateway.GetEffects();
        CheckAssertions.Equal("malformed_proposal", (await Execute(run, new((Operation)999, "documents/task"))).Reason);
        CheckAssertions.Equal("malformed_proposal", (await Execute(run, new(Operation.ReadDocument, "documents/task", -1))).Reason);
        CheckAssertions.Equal("out_of_scope", (await Execute(run, new(Operation.ReadDocument, "documents/../secrets"))).Reason);
        CheckAssertions.Equal(before, gateway.GetEffects());
    }
}
