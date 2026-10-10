namespace SecureAgentLab.TransportChecks.Fixtures;

internal delegate Task<HttpResponseMessage> SendCallback(string? token, HttpMethod method, string path, object? body = null);
