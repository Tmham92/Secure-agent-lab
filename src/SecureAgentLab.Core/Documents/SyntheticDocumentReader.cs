using System.Text;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;

namespace SecureAgentLab.Core.Documents;

public sealed class SyntheticDocumentReader : IDocumentReader
{
    public Decision Read(string resource, long remainingBytes) => resource != "documents/task" ? new(Outcome.Denied, "document_out_of_scope") : Encoding.UTF8.GetByteCount(Gateway.TaskDocument) > remainingBytes ? new(Outcome.Denied, "response_budget_exhausted") : new(Outcome.Allowed, "scoped_read", Gateway.TaskDocument);
}
