using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SecureAgentLab.Core.Diagnostics;

/// <summary>Retains one disposable lab artifact set per case; never use for production audit retention.</summary>
public sealed class ArtifactRun : IDisposable
{
    private static readonly ConcurrentDictionary<string, int> FixtureNumbers = new(StringComparer.Ordinal);
    private readonly FileStream _lease;
    private readonly DateTimeOffset _started;
    private bool _disposed;

    public string DirectoryPath
    {
        get;
    }
    public long RunNumber
    {
        get;
    }
    public string CaseName
    {
        get;
    }

    private ArtifactRun(string directory, string caseName, long number, FileStream lease)
    {
        DirectoryPath = directory;
        CaseName = caseName;
        RunNumber = number;
        _lease = lease;
        _started = DateTimeOffset.UtcNow;
        WriteMetadata(null);
    }

    public static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecureAgentLab.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Run from a repository build.");
    }

    public static ArtifactRun StartForAssembly(string caseName) => Start(FindRepository(), caseName);

    public static string PathForAssembly(string caseName) => Path.GetFullPath(Path.Combine(FindRepository(), "artifacts", ValidateCase(caseName), "latest"));

    public static ArtifactRun Start(string repository, string caseName)
    {
        ValidateCase(caseName);
        string artifacts = Path.GetFullPath(Path.Combine(repository, "artifacts"));
        string registry = Path.GetFullPath(Path.Combine(artifacts, ".runs", caseName));
        string directory = Path.GetFullPath(Path.Combine(artifacts, caseName, "latest"));
        EnsurePlainAncestors(Path.GetDirectoryName(registry)!);
        EnsurePlainAncestors(Path.GetDirectoryName(directory)!);
        RejectLink(registry + ".lock");
        FileStream lease;
        try
        {
            lease = new FileStream(registry + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Artifact case is already active or its lease is unavailable: " + caseName, exception);
        }
        try
        {
            RejectLink(registry + ".json");
            RejectLink(registry + ".tmp");
            long previous = File.Exists(registry + ".json") ? JsonSerializer.Deserialize<long>(File.ReadAllText(registry + ".json")) : 0;
            if (previous < 0)
            {
                throw new InvalidDataException("Invalid artifact run counter.");
            }
            long number = checked(previous + 1);
            File.WriteAllText(registry + ".tmp", JsonSerializer.Serialize(number));
            File.Move(registry + ".tmp", registry + ".json", true);
            // This exact generated path is within artifacts. Links inside it are removed without traversing targets.
            RemoveOwnedEntry(directory);
            Directory.CreateDirectory(directory);
            foreach (string key in FixtureNumbers.Keys.Where(key => key.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || key == directory))
            {
                FixtureNumbers.TryRemove(key, out _);
            }
            return new ArtifactRun(directory, caseName, number, lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public static string FixturePath(string root)
    {
        string full = Path.GetFullPath(root);
        int number = FixtureNumbers.AddOrUpdate(full, 1, (_, value) => checked(value + 1));
        return Path.Combine(full, "case-" + number.ToString("D3", CultureInfo.InvariantCulture));
    }

    private static string ValidateCase(string caseName)
    {
        if (caseName.Length > 120 || !Regex.IsMatch(caseName, "^[a-z0-9]+(?:-[a-z0-9]+)*(?:/[a-z0-9]+(?:-[a-z0-9]+)*)*$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Artifact case must contain fixed lowercase path segments.", nameof(caseName));
        }
        return caseName;
    }

    private static void EnsurePlainAncestors(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        var parents = new Stack<DirectoryInfo>();
        while (directory is not null)
        {
            parents.Push(directory);
            directory = directory.Parent;
        }
        foreach (DirectoryInfo parent in parents)
        {
            RejectLink(parent.FullName);
            Directory.CreateDirectory(parent.FullName);
        }
    }

    private static void RejectLink(string path)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException) { return; }
        catch (DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Artifact ownership path must not be a link.");
        }
    }

    private static void RemoveOwnedEntry(string path)
    {
        if (!Path.Exists(path))
        {
            return;
        }
        FileAttributes attributes = File.GetAttributes(path);
        bool directory = (attributes & FileAttributes.Directory) != 0;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            if (directory)
            {
                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }
            return;
        }
        if (directory)
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
            {
                RemoveOwnedEntry(child);
            }
            Directory.Delete(path);
        }
        else
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
    }

    private void WriteMetadata(DateTimeOffset? finished) => File.WriteAllText(Path.Combine(DirectoryPath, "run.json"), JsonSerializer.Serialize(new
    {
        Case = CaseName,
        RunNumber,
        StartedUtc = _started,
        FinishedUtc = finished
    }, new JsonSerializerOptions { WriteIndented = true }));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            WriteMetadata(DateTimeOffset.UtcNow);
        }
        finally { _lease.Dispose(); }
    }
}
