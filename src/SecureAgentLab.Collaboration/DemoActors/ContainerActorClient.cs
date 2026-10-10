using System.Net.Http.Headers;

namespace SecureAgentLab.Collaboration.DemoActors;

internal static class ContainerActorClient
{
    internal static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing " + name);
    internal static void Check(bool value, string name)
    {
        if (!value)
        {
            throw new InvalidOperationException(name);
        }
    }

    internal static HttpRequestMessage Request(string token, string path, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    internal static async Task<T> Post<T>(HttpClient client, string token, string path, object body)
    {
        using HttpRequestMessage req = Request(token, path, body);
        using HttpResponseMessage res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }
}
