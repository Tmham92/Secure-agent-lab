namespace SecureAgentLab.Collaboration.Contracts;

public sealed record Evaluation(bool AcceptedEvidence, bool CorrectAnswer, bool AuthorizedMethod, bool Passed, string Reason);
