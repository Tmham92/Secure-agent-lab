using System.Diagnostics;
using SecureAgentLab.Core.Documents;
using SecureAgentLab.Core.Execution;
using SecureAgentLab.Core.Grants;
using SecureAgentLab.Core.Policy;

namespace SecureAgentLab.Comparisons.Fixtures;
// No user-supplied paths, targets, payloads, model outputs or commands are accepted by this executable.
public sealed class ComparisonFixture
{
    public const string FakeSecret = "FAKE_SECRET_COMPARISON_ONLY";
    public const string Reviewed = "Reviewed harmless synthetic draft";
    public const string Replaced = "Unreviewed harmless synthetic replacement";
    public string Directory
    {
        get;
    }
    public string Documents => Path.Combine(Directory, "documents");

    public ComparisonFixture(string root, string scenario, string variant)
    {
        Directory = Path.Combine(root, scenario, variant);
        System.IO.Directory.CreateDirectory(Path.Combine(Documents, "task"));
        System.IO.Directory.CreateDirectory(Path.Combine(Documents, "reference"));
        File.WriteAllText(Path.Combine(Documents, "task", "task.txt"), Gateway.TaskDocument);
        File.WriteAllText(Path.Combine(Documents, "reference", "reference.txt"), SyntheticDocuments.Reference);
        File.WriteAllText(Path.Combine(Directory, "private.txt"), FakeSecret);
    }

    public Gateway ReaderGateway() => new(new DefaultPolicy(), new DocumentExecutor(new FileDocumentReader(Documents, SyntheticDocuments.Catalog)));
    public static string Run(Gateway g) => g.CreateSession(TaskGrant.Default(DateTimeOffset.UtcNow.AddMinutes(3), 30, 8192));
    public void Link()
    {
        string target = Path.Combine(Directory, "private");
        System.IO.Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "secret.txt"), FakeSecret);
        string link = Path.Combine(Documents, "link");
        if (!OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateSymbolicLink(link, target);
            return;
        }

        // A directory junction needs no symlink privilege. Both paths are generated within this fixture.
        if ((link + target).Any(c => c is '"' or '&' or '|' or '<' or '>' or '%' or '^' or '\n' or '\r'))
        {
            throw new InvalidOperationException("Unsafe fixture path");
        }

        var info = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"";
        using Process p = Process.Start(info)!;
        if (!p.WaitForExit(5000) || p.ExitCode != 0)
        {
            throw new InvalidOperationException("Cannot create synthetic junction fixture");
        }
    }
}
