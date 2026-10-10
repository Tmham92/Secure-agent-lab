namespace SecureAgentLab.Durable.Recovery;

public sealed class AuthorizationDependencyException(string reason, Exception? inner = null) : Exception(reason, inner);
