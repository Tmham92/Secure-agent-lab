using SecureAgentLab.ModelHost.Clients;

namespace SecureAgentLab.ModelHost.Hosting;

internal static class ModelHostApplication
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Configure LAB_OPENAI_MODEL and OPENAI_API_KEY on the trusted host only.");
            return 1;
        }

        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
            var output = await new ResponsesProposalClient(client).GenerateAsync(Environment.GetEnvironmentVariable("LAB_OPENAI_MODEL") ?? "", Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "");
            Console.Write(System.Text.Encoding.UTF8.GetString(output)); // Caller captures privately to a bounded proposal artifact.
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or HttpRequestException or OperationCanceledException or global::SecureAgentLab.Core.Proposals.ModelOutputException)
        {
            Console.Error.WriteLine("model_generation_failed");
            return 1;
        }
    }
}
