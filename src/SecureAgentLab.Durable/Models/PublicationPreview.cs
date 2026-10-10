namespace SecureAgentLab.Durable.Models;

public sealed record PublicationPreview(string Destination, string Content, string ContentHash, string PolicyVersion, long? ExpectedVersion = null);
