namespace SecureAgentLab.AuditCollector.Configuration;

public sealed record CollectorSettings(string LogDirectory, string CheckpointDirectory, string StreamId, string SigningPrivateKey, string WriteKey, bool AllowLoopbackHttp = false);
