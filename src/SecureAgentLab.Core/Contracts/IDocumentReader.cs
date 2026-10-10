namespace SecureAgentLab.Core.Contracts;

public interface IDocumentReader
{
    Decision Read(string resource, long remainingBytes);
}
