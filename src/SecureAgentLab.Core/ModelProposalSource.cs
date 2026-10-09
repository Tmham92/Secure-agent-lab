using System.Text.Json;

namespace SecureAgentLab.Core;

public sealed class ModelOutputException : Exception
{
    public ModelOutputException() : base("model_output_rejected") { }
}

// Fully validate the entire bounded batch before exposing any proposal. Never deserialize authority.
public sealed class ModelProposalSource : IProposalSource
{
    public const int MaximumBytes = 16384;
    private readonly Proposal[] proposals;
    public ModelProposalSource(byte[] utf8)
    {
        if (utf8.Length > MaximumBytes) throw new ModelOutputException();
        try
        {
            using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = 8 });
            Exact(document.RootElement, "proposals");
            var batch = document.RootElement.GetProperty("proposals");
            if (batch.ValueKind != JsonValueKind.Array || batch.GetArrayLength() is < 1 or > 8) throw new ModelOutputException();
            proposals = batch.EnumerateArray().Select(item =>
            {
                Exact(item, "operation", "resource");
                var operation = item.GetProperty("operation");
                var resource = item.GetProperty("resource");
                if (operation.ValueKind != JsonValueKind.String || resource.ValueKind != JsonValueKind.String ||
                    !Enum.TryParse<Operation>(operation.GetString(), false, out var op) || !Enum.IsDefined(op) ||
                    operation.GetString() != op.ToString()) throw new ModelOutputException();
                var id = resource.GetString()!;
                if (id.Length is < 1 or > 256 || id.Any(char.IsControl) || string.IsNullOrWhiteSpace(id)) throw new ModelOutputException();
                return new Proposal(op, id);
            }).ToArray();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { throw new ModelOutputException(); }
    }
    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ModelOutputException();
        var keys = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (keys.Length != names.Length || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length ||
            names.Any(n => !keys.Contains(n, StringComparer.Ordinal))) throw new ModelOutputException();
    }
    public IEnumerable<Proposal> GetProposals() => Array.AsReadOnly(proposals);
    public static ModelProposalSource FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[MaximumBytes + 1];
        var count = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return new(buffer[..count]);
    }
}
