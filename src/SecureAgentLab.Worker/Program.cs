using System.Net.Http.Headers;
using System.Net.Http.Json;
using SecureAgentLab.Core;
using SecureAgentLab.Transport;

if (args.Contains("--isolation-checks")) await IsolationChecks.Run();
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
{ Console.Error.WriteLine("worker_secret_configuration_rejected"); return 1; }

var endpoint = Environment.GetEnvironmentVariable("LAB_GATEWAY_URL") ?? "https://localhost:7240";
var credential = Environment.GetEnvironmentVariable("LAB_WORKER_CREDENTIAL");
if (string.IsNullOrWhiteSpace(credential)) throw new InvalidOperationException("LAB_WORKER_CREDENTIAL is required.");
var uri = new Uri(endpoint);
if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback &&
    Environment.GetEnvironmentVariable("LAB_ALLOW_LOOPBACK_HTTP") == "true"))
    throw new InvalidOperationException("HTTPS is required outside the explicit loopback-only lab.");
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(10) };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
var proposalFile = Environment.GetEnvironmentVariable("LAB_MODEL_PROPOSALS_FILE");
if (proposalFile == "") proposalFile = null;
IProposalSource source;
try { source = proposalFile is null ? new DeterministicProposalSource() : ModelProposalSource.FromFile(proposalFile); }
catch (Exception e) when (e is ModelOutputException or IOException or UnauthorizedAccessException or ArgumentException)
{ Console.Error.WriteLine("model_output_rejected"); return 1; }
var expected = new[] { Outcome.Allowed, Outcome.Denied, Outcome.Denied, Outcome.Denied, Outcome.ApprovalRequired };
var index = 0;
foreach (var proposal in source.GetProposals())
{
    using var response = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(proposal));
    response.EnsureSuccessStatusCode();
    var decision = await response.Content.ReadFromJsonAsync<Decision>() ?? throw new InvalidOperationException("Missing decision.");
    Console.WriteLine($"{proposal.Operation}: {decision.Outcome} ({decision.Reason})");
    if (proposalFile is null && decision.Outcome != expected[index++]) return 1;
}
return 0;
