using System.Text;
using SecureAgentLab.Comparisons.Fixtures;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Core.Execution;

namespace SecureAgentLab.Comparisons.Vulnerable;
// Educational fixtures ONLY. Never referenced by the normal gateway, worker or API.
// Outer bounds stay intact: only named synthetic resources, at most 6 attempts and generated files.
public sealed class DeliberatelyVulnerable
{
    public List<string> Capture { get; } = [];
    public List<string> Publications { get; } = [];
    public int Messages
    {
        get; private set;
    }
    public bool Admin
    {
        get; private set;
    }

    private bool publicationApproved;
    public void ApprovePublishOperation() => publicationApproved = true;
    public string ReadTask(string authenticatedTask, string selectedTask) => selectedTask switch
    {
        "task-a" => Gateway.TaskDocument,
        "task-b" => ComparisonFixture.FakeSecret,
        _ => throw new ArgumentException("Only synthetic task IDs")
    };
    public void Execute(Proposal p)
    {
        if (p.Resource != "synthetic-local-store")
        {
            throw new ArgumentException("No arbitrary destinations");
        }

        switch (p.Operation)
        {
            case Operation.ExternalRequest:
                Capture.Add(ComparisonFixture.FakeSecret);
                break; // In-memory store, never HTTP.
            case Operation.MessageAgent:
                Messages++;
                break;
            case Operation.ChangePermissions:
                Admin = true;
                break;
            default:
                throw new ArgumentException("Unknown synthetic operation");
        }
    }

    public void PublishWithOperationOnlyApproval(string content)
    {
        if (content is not (ComparisonFixture.Reviewed or ComparisonFixture.Replaced))
        {
            throw new ArgumentException("Synthetic drafts only");
        }

        if (!publicationApproved)
        {
            throw new InvalidOperationException("Host operation approval required");
        }

        publicationApproved = false;
        Publications.Add(content);
    }

    public static string ReadPath(ComparisonFixture fixture, string relative, long declaredBytes)
    {
        if (relative is not ("../private.txt" or "link/secret.txt" or "task/large.txt" or "task/task.txt"))
        {
            throw new ArgumentException("Fixed fixture paths only");
        }

        string path = Path.GetFullPath(Path.Combine(fixture.Documents, relative));
        if (!path.StartsWith(fixture.Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Outer fixture boundary");
        }

        if (declaredBytes > 128)
        {
            throw new InvalidOperationException("Declared cap");
        }

        if (relative == "link/secret.txt" && new DirectoryInfo(Path.Combine(fixture.Documents, "link")).ResolveLinkTarget(true)?.FullName != Path.Combine(fixture.Directory, "private"))
        {
            throw new InvalidOperationException("Only generated synthetic link targets");
        }

        using FileStream file = File.OpenRead(path);
        byte[] bytes = new byte[8193];
        int count = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > 8192)
        {
            throw new InvalidOperationException("Outer demonstration cap");
        }

        return Encoding.UTF8.GetString(bytes, 0, count);
    }
}
