using SecureAgentLab.Core.Execution;
namespace SecureAgentLab.DocumentChecks.Fixtures;

internal delegate string GrantCallback(Gateway g, string id, long bytes = 4096);
