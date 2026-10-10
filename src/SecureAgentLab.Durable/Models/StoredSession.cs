namespace SecureAgentLab.Durable.Models;

public sealed record StoredSession(StoredGrant Grant, int Calls, long Bytes, bool Revoked = false);
