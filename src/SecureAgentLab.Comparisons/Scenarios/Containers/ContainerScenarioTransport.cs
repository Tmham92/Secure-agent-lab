namespace SecureAgentLab.Comparisons.Scenarios.Containers;

internal static class ContainerScenarioTransport
{
    internal static HttpClient Client(string uri, int seconds = 3) => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        BaseAddress = new Uri(uri),
        Timeout = TimeSpan.FromSeconds(seconds)
    };
    internal static async Task Until(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready())
        {
            await Task.Delay(10, deadline.Token); // Real IPC readiness, not an identity-expiry test.
        }
    }
}
