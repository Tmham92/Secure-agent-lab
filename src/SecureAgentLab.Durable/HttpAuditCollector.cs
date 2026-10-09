using System.Net.Http.Headers;
using System.Net.Http.Json;
using SecureAgentLab.Core;

namespace SecureAgentLab.Durable;

public sealed class HttpAuditCollector : IAuditCollector, IDisposable
{
    private readonly HttpClient client;
    public HttpAuditCollector(string address, string writeKey, bool allowLoopbackHttp = false)
    {
        var uri = new Uri(address);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback && allowLoopbackHttp))
            throw new ArgumentException("Audit HTTPS is required except in an explicit loopback lab.");
        if (Convert.FromBase64String(writeKey).Length < 32) throw new ArgumentException("Audit writer key must have at least 32 random bytes.");
        client = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", writeKey);
    }
    private T Send<T>(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = client.Send(request);
        if (!response.IsSuccessStatusCode) throw new AuthorizationDependencyException("audit_service_unavailable");
        return response.Content.ReadFromJsonAsync<T>().GetAwaiter().GetResult()
            ?? throw new AuthorizationDependencyException("audit_response_invalid");
    }
    public SignedCheckpoint GetCheckpoint() => Send<SignedCheckpoint>(HttpMethod.Get, "/checkpoint");
    public SignedCheckpoint Append(AuditEntry entry) => Send<SignedCheckpoint>(HttpMethod.Post, "/events", entry);
    public IReadOnlyList<SignedAuditEvent> GetEvents() => Send<List<SignedAuditEvent>>(HttpMethod.Get, "/events").AsReadOnly();
    public void Dispose() => client.Dispose();
}
