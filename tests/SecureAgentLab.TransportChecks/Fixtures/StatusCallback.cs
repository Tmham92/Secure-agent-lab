using System.Net;
namespace SecureAgentLab.TransportChecks.Fixtures;

internal delegate Task StatusCallback(string? token, string path, object body, HttpStatusCode status);
