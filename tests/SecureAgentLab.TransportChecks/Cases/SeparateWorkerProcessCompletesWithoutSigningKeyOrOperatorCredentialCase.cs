using System.Diagnostics;
using SecureAgentLab.Transport.Responses;
using SecureAgentLab.TransportChecks.Fixtures;

namespace SecureAgentLab.TransportChecks.Cases;

internal static class SeparateWorkerProcessCompletesWithoutSigningKeyOrOperatorCredentialCase
{
    internal static async Task RunAsync(string address, IssueCallback Issue)
    {
        IssuedRun run = await Issue();
        string root = CheckAssertions.FindRepository();
        string config = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar) ? "Release" : "Debug";
        string workerDll = Path.Combine(root, "src", "SecureAgentLab.Worker", "bin", config, "net10.0", "SecureAgentLab.Worker.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(workerDll);
        foreach (string? key in start.Environment.Keys.Where(k => k.StartsWith("Lab__", StringComparison.OrdinalIgnoreCase) || k.StartsWith("LAB_", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            start.Environment.Remove(key);
        }

        start.Environment["LAB_WORKER_CREDENTIAL"] = run.WorkerCredential;
        start.Environment["LAB_GATEWAY_URL"] = address;
        start.Environment["LAB_ALLOW_LOOPBACK_HTTP"] = "true";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        CheckAssertions.
                Equal(0, process.ExitCode);
        string output = await stdout;
        string errors = await stderr;
        CheckAssertions.Assert(output.Contains("ReadDocument: Allowed") && output.Contains("PublishReport: ApprovalRequired"), "Missing worker walkthrough");
        CheckAssertions.Assert(!output.Contains(run.WorkerCredential) && !errors.Contains(run.WorkerCredential), "Credential leaked by worker");
    }
}
