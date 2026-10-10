using SecureAgentLab.Core.Documents;
namespace SecureAgentLab.DocumentChecks.Fixtures;

internal delegate FileDocumentReader ReaderCallback(string root, int max = 4096);
