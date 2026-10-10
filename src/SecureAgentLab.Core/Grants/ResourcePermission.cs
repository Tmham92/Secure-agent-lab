using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Core.Grants;

public sealed record ResourcePermission(Operation Operation, string Resource);
