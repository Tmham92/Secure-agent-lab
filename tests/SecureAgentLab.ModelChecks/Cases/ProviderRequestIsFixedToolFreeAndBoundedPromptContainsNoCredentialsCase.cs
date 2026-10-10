using System.Net;
using System.Text;
using System.Text.Json;
using SecureAgentLab.ModelChecks.Fixtures;
using SecureAgentLab.ModelHost.Clients;

namespace SecureAgentLab.ModelChecks.Cases;

internal static class ProviderRequestIsFixedToolFreeAndBoundedPromptContainsNoCredentialsCase
{
    internal static async Task RunAsync(string Valid, EnvelopeCallback Envelope)
    {
        var handler = new FakeHandler(async request =>
        {
            CheckAssertions.Equal(ResponsesProposalClient.Endpoint, request.RequestUri);
            CheckAssertions.Equal(HttpMethod.Post, request.Method);
            CheckAssertions.Equal("synthetic-provider-key", request.Headers.Authorization!.Parameter);
            string body = await request.Content!.ReadAsStringAsync();
            CheckAssertions.Assert(!body.Contains("synthetic-provider-key"), "Key leaked into prompt");
            using var json = JsonDocument.Parse(body);
            JsonElement root = json.RootElement;
            CheckAssertions.Equal(false, root.GetProperty("store").GetBoolean());
            CheckAssertions.Equal(1024, root.GetProperty("max_output_tokens").GetInt32());
            CheckAssertions.Equal(0, root.GetProperty("tools").GetArrayLength());
            CheckAssertions.Equal("json_schema", root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Envelope(Valid))
            };
        });
        using var client = new HttpClient(handler);
        CheckAssertions.Equal(Valid, Encoding.UTF8.GetString(await new ResponsesProposalClient(client).Generate("synthetic-model", "synthetic-provider-key")));
        CheckAssertions.Equal(1, handler.Calls);
    }
}
