using SecureAgentLab.ModelHost;

if (args.Length != 0) { Console.Error.WriteLine("Configure LAB_OPENAI_MODEL and OPENAI_API_KEY on the trusted host only."); return 1; }
try
{
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    var output = await new ResponsesProposalClient(client).Generate(
        Environment.GetEnvironmentVariable("LAB_OPENAI_MODEL") ?? "", Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "");
    Console.Write(System.Text.Encoding.UTF8.GetString(output)); // Caller captures privately to a bounded proposal artifact.
    return 0;
}
catch (Exception e) when (e is ArgumentException or HttpRequestException or OperationCanceledException or SecureAgentLab.Core.ModelOutputException)
{ Console.Error.WriteLine("model_generation_failed"); return 1; }
