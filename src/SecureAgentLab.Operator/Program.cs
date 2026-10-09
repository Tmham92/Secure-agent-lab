using SecureAgentLab.Transport;

if (args.Length != 1 || args[0] is not ("issue-operator" or "isolation-run" or "isolation-effects"))
{
    Console.Error.WriteLine("Usage: issue-operator | isolation-run | isolation-effects (trusted host only)");
    return 1;
}
var settings = new CredentialSettings(Environment.GetEnvironmentVariable("Lab__Issuer") ?? "",
    Environment.GetEnvironmentVariable("Lab__Audience") ?? "", Environment.GetEnvironmentVariable("Lab__SigningKey") ?? "");
// This one explicit command prints a bearer credential for the trusted operator.
// Capture it privately; never run this issuer inside a worker workload or CI logs.
var token = new CredentialService(settings).Issue("lab-operator", "operator", TimeSpan.FromMinutes(5));
if (args[0] == "issue-operator") Console.WriteLine(token);
else
{
    if (args[0] == "isolation-run")
    {
        // Positive controls: protected fixture and both backend address families really exist.
        if (File.ReadAllText("/protected/other-run.txt") != "synthetic protected canary")
            throw new InvalidOperationException("Protected storage fixture missing.");
        using var ipv6 = new System.Net.Sockets.TcpClient(System.Net.Sockets.AddressFamily.InterNetworkV6);
        await ipv6.ConnectAsync(System.Net.IPAddress.IPv6Loopback, 5188);
    }
    using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5188"), Timeout = TimeSpan.FromSeconds(3) };
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    using var response = args[0] == "isolation-run"
        ? await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, "/operator/runs", new IssueRunRequest())
        : await client.GetAsync("/operator/effects");
    response.EnsureSuccessStatusCode();
    Console.WriteLine(await response.Content.ReadAsStringAsync());
}
return 0;
