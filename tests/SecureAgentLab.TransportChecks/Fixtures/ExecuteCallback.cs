using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Transport.Responses;
namespace SecureAgentLab.TransportChecks.Fixtures;

internal delegate Task<Decision> ExecuteCallback(IssuedRun run, Proposal proposal, string? ticket = null);
