using System.Text;
using SecureAgentLab.Core.Contracts;

namespace SecureAgentLab.Core.Documents;
/// <summary>Host-configured exact IDs, pinned bytes and bounded strict UTF-8. No caller paths or URLs.</summary>
public sealed class FileDocumentReader : IDocumentReader
{
    private readonly string root;
    private readonly Dictionary<string, DocumentEntry> entries;
    private readonly int maxBytes;
    public FileDocumentReader(string root, IEnumerable<DocumentEntry> catalog, int maxBytes = 4096)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || maxBytes is < 1 or > 65536)
        {
            throw new ArgumentException("Invalid document configuration.");
        }

        this.root = Path.GetFullPath(root);
        if (OperatingSystem.IsWindows() && !char.IsAsciiLetter(this.root[0]))
        {
            throw new ArgumentException("Document root must be a local drive path.");
        }

        this.maxBytes = maxBytes;
        entries = new(StringComparer.Ordinal);
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (DocumentEntry? entry in catalog)
        {
            if (entry is null || entry.Resource is not ("documents/task" or "documents/reference") || string.IsNullOrWhiteSpace(entry.RelativePath) || entry.RelativePath.Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) || string.IsNullOrEmpty(entry.Sha256) || entry.Sha256.Length != 64 || entry.Sha256.Any(c => !Uri.IsHexDigit(c)) || !entries.TryAdd(entry.Resource, entry))
            {
                throw new ArgumentException("Invalid document catalog.");
            }
        }

        if (entries.Count == 0)
        {
            throw new ArgumentException("Empty document catalog.");
        }
    }

    public Decision Read(string resource, long remainingBytes)
    {
        if (remainingBytes < 0 || !entries.TryGetValue(resource, out DocumentEntry? entry))
        {
            return Deny("document_out_of_scope");
        }

        try
        {
            using Stream file = SafeDocumentFile.Open(root, entry.RelativePath);
            int limit = (int)Math.Min(maxBytes, remainingBytes);
            byte[] bytes = new byte[limit + 1];
            int count = 0;
            while (count < bytes.Length)
            {
                int read = file.Read(bytes, count, bytes.Length - count);
                if (read == 0)
                {
                    break;
                }

                count += read;
            }

            if (count > limit)
            {
                return Deny(remainingBytes < maxBytes ? "response_budget_exhausted" : "document_too_large");
            }

            byte[] body = bytes[..count];
            if (!string.Equals(SyntheticDocuments.Hash(body), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Deny("document_version_mismatch");
            }

            return new(Outcome.Allowed, "scoped_read", new UTF8Encoding(false, true).GetString(body));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or DecoderFallbackException)
        {
            return Deny("document_unavailable");
        } // No local paths, exception details or partially read bytes escape.
    }

    private static Decision Deny(string reason) => new(Outcome.Denied, reason);
}
