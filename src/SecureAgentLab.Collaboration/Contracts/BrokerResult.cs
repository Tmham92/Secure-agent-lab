namespace SecureAgentLab.Collaboration.Contracts;

public sealed record BrokerResult(bool Allowed, string Reason, Envelope? Message = null);
