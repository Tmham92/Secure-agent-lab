using System.Net;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Fixtures;
using SecureAgentLab.ModelHost.Clients;
namespace SecureAgentLab.ModelChecks.Cases;

internal static class ProviderRedirectHTTPErrorAndOversizedBodyRejectedWithoutRetriesCase
{
    internal static async Task RunAsync()
    {
        foreach (HttpStatusCode status in new[]
        {
            HttpStatusCode.Redirect,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.OK
        }

        )
        {
            var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(new string('X', 70000)) }));
            using var client = new HttpClient(handler);
            try
            {
                await new ResponsesProposalClient(client).Generate("synthetic-model", "synthetic-key");
                throw new InvalidOperationException("Unexpected success");
            }
            catch (ModelOutputException)
            {
            }
            CheckAssertions.
                        Equal(1, handler.Calls);
        }
    }
}
