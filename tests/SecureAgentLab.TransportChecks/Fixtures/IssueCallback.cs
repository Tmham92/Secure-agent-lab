using SecureAgentLab.Transport.Requests;
using SecureAgentLab.Transport.Responses;
namespace SecureAgentLab.TransportChecks.Fixtures;

internal delegate Task<IssuedRun> IssueCallback(IssueRunRequest? body = null);
