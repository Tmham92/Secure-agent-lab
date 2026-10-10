namespace SecureAgentLab.Collaboration.Contracts;
// Authority comes from authentication, never from message text or a caller-supplied role.
public sealed record Envelope(long Id, string Run, string Sender, string Recipient, string Topic, string Text, string Role = "worker-data");
