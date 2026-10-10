namespace SecureAgentLab.Collaboration.Contracts;

public sealed record SendMessage(string Recipient, string Topic, string Text);
