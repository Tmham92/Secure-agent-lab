using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SecureAgentLab.Playground.Advanced;

// A fixed child executable boundary keeps deliberately vulnerable comparisons out of the web host.
public sealed class AdvancedProcessRunner(string repositoryRoot, string configuration)
{
    private static readonly SemaphoreSlim Admission = new(1, 1);
    public static string FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SecureAgentLab.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Run the playground from its repository build.");
    }
    public async Task<AdvancedResult> RunAsync(string id, CancellationToken cancellationToken = default)
    {
        AdvancedScenario scenario = AdvancedCatalog.Find(id);
        if (scenario.Project is null)
        {
            return new(id, "Run with Docker launcher", "Follow the displayed command in a repository terminal. Docker Linux containers are required.", null, 0);
        }
        if (!await Admission.WaitAsync(0, cancellationToken))
        {
            return new(id, "Runner busy", "Another browser session is running an advanced exercise. Retry after it finishes.", null, 0);
        }
        var watch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            if (configuration is not ("Release" or "Debug"))
            {
                throw new InvalidOperationException("Unsupported build configuration.");
            }
            string projectName = Path.GetFileName(scenario.Project);
            string assembly = Path.Combine(repositoryRoot, scenario.Project, "bin", configuration, "net10.0", projectName + ".dll");
            if (!File.Exists(assembly))
            {
                return new(id, "Build required", $"Build the complete solution first: dotnet build SecureAgentLab.slnx -c {configuration}", null, watch.ElapsedMilliseconds);
            }
            var info = new ProcessStartInfo("dotnet") { WorkingDirectory = repositoryRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            string[] inherited = ["PATH", "SystemRoot", "WINDIR", "SystemDrive", "TEMP", "TMP", "HOME", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "ProgramFiles", "ProgramFiles(x86)", "DOTNET_ROOT", "DOTNET_ROOT(x86)", "PROCESSOR_ARCHITECTURE", "COMSPEC"];
            var environment = inherited.Select(key => (Key: key, Value: Environment.GetEnvironmentVariable(key))).ToArray();
            info.Environment.Clear();
            foreach (var entry in environment)
            {
                if (entry.Value is not null)
                {
                    info.Environment[entry.Key] = entry.Value;
                }
            }
            info.Environment["DOTNET_NOLOGO"] = "1";
            info.ArgumentList.Add(assembly);
            foreach (string argument in scenario.Arguments)
            {
                info.ArgumentList.Add(argument);
            }
            using var process = new Process { StartInfo = info };
            process.Start();
            var output = new StringBuilder();
            object outputLock = new();
            bool truncated = false;
            async Task Drain(StreamReader stream)
            {
                char[] buffer = new char[2048];
                int count;
                while ((count = await stream.ReadAsync(buffer.AsMemory(), deadline.Token)) != 0)
                {
                    lock (outputLock)
                    {
                        int remaining = 131072 - output.Length;
                        if (remaining > 0)
                        {
                            output.Append(buffer, 0, Math.Min(count, remaining));
                        }
                        if (count > remaining)
                        {
                            truncated = true;
                            deadline.Cancel();
                        }
                    }
                }
            }
            Task stdout = Drain(process.StandardOutput), stderr = Drain(process.StandardError);
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                try
                {
                    await Task.WhenAll(stdout, stderr);
                }
                catch (OperationCanceledException) { }
                return new(id, truncated ? "Output limit reached" : "Cancelled or timed out", output.ToString(), null, watch.ElapsedMilliseconds);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            string captured = output.ToString();
            AdvancedComparison? comparison = null;
            const string marker = "PLAYGROUND_RESULT:";
            string? line = captured.Split('\n').SingleOrDefault(x => x.StartsWith(marker, StringComparison.Ordinal));
            if (line is not null)
            {
                comparison = JsonSerializer.Deserialize<AdvancedComparison>(line[marker.Length..]);
                captured = string.Join('\n', captured.Split('\n').Where(x => !x.StartsWith(marker, StringComparison.Ordinal)));
            }
            bool passed = process.ExitCode == 0 && (scenario.Category != "Baseline vs hardened" || comparison is { Passed: true, Unsafe.PositiveControl: true, Secure.PositiveControl: true });
            return new(id, passed ? "Passed" : "Failed", captured.Trim(), process.ExitCode, watch.ElapsedMilliseconds, comparison);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            return new(id, "Runner unavailable", "The fixed runner could not complete. Build the solution and inspect the scenario's CLI output. " + e.GetType().Name, null, watch.ElapsedMilliseconds);
        }
        finally { Admission.Release(); }
    }
}

