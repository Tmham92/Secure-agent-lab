using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using SecureAgentLab.Core;
using static SecureAgentLab.Comparisons.ComparisonEvidence;

namespace SecureAgentLab.Comparisons;

public static class ContainerScenarios
{
    public static async Task Run(string[] args)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/outer-contained") || args.Length != 1) throw new InvalidOperationException("Fixed disposable Linux comparison container required");
        var mode = args[0];
        if (mode == "server") { await Server(); return; }
        if (mode == "readiness")
        {
            using var relay = Client("http://127.0.0.1:8080");
            Require(await relay.GetStringAsync("/read") == Gateway.TaskDocument, "relay readiness");
            foreach (var url in new[] { "http://169.254.169.254", "http://172.17.0.1", "http://203.0.113.1", "http://[2001:db8::1]" })
            {
                using var probe = Client(url, 1);
                try { using var response = await probe.GetAsync("/"); throw new InvalidOperationException("Outer boundary allowed direct route"); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
            }
            return;
        }
        if (mode == "idle") { await Task.Delay(Timeout.InfiniteTimeSpan); return; }
        if (mode == "late-child")
        {
            File.WriteAllText("/workspace/ready", "ready");
            await Until(() => File.Exists("/workspace/release"));
            File.WriteAllText("/workspace/late", DateTimeOffset.UtcNow.ToString("O")); return;
        }
        if (mode is "containment-unsafe" or "containment-secure") { await Containment(mode.EndsWith("unsafe", StringComparison.Ordinal)); return; }
        if (mode is "isolation-unsafe" or "isolation-secure") { await Isolation(mode.EndsWith("unsafe", StringComparison.Ordinal)); return; }
        if (mode == "inspect") { await Inspect(); return; }
        throw new ArgumentException("Unknown fixed container role");
    }
    private static HttpClient Client(string uri, int seconds = 3) => new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    { BaseAddress = new Uri(uri), Timeout = TimeSpan.FromSeconds(seconds) };
    private static async Task Server()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024);
        var app = builder.Build();
        var gate = new object(); var capture = new Dictionary<string, List<string>> { ["unsafe"] = [], ["secure"] = [] };
        app.MapGet("/read", () => Gateway.TaskDocument);
        app.MapPost("/capture/{variant}", (string variant, Capture body) =>
        {
            lock (gate)
            {
                if (!capture.TryGetValue(variant, out var bucket) || body.Text != ComparisonFixture.FakeSecret || bucket.Count >= 1) return Results.BadRequest();
                bucket.Add(body.Text); return Results.Ok();
            }
        });
        app.MapGet("/effects", () => { lock (gate) return capture.ToDictionary(p => p.Key, p => p.Value.ToArray()); });
        await app.RunAsync();
    }
    private static async Task Isolation(bool insecure)
    {
        using var relay = Client("http://127.0.0.1:8080");
        Require(await relay.GetStringAsync("/read") == Gateway.TaskDocument, "authorized relay read failed");
        var exists = File.Exists("/fake/secret.txt");
        Require(exists == insecure, "wrong synthetic mount isolation");
        var content = exists ? File.ReadAllText("/fake/secret.txt") : ComparisonFixture.FakeSecret;
        Require(content == ComparisonFixture.FakeSecret, "non-synthetic fixture rejected");
        using var direct = Client("http://127.0.0.1:9000", 1);
        var transferred = false;
        try
        {
            using var response = await direct.PostAsJsonAsync("/capture/" + (insecure ? "unsafe" : "secure"), new Capture(content));
            if (!insecure) throw new InvalidOperationException("Direct capture returned HTTP; application denial is not network isolation");
            response.EnsureSuccessStatusCode(); transferred = true;
        }
        catch (Exception e) when (!insecure && e is HttpRequestException or TaskCanceledException) { }
        Require(transferred == insecure, "network isolation did not differ as expected");
        File.WriteAllText("/workspace/isolation.json", JsonSerializer.Serialize(new { Insecure = insecure, FakeMountPresent = exists, Transferred = transferred, PositiveRead = true }));
        Console.WriteLine(insecure ? "PASS unsafe: fake mount read and direct local capture transfer observed" : "PASS secure: fake mount absent, direct capture blocked, permitted relay read succeeds");
    }
    private static async Task Containment(bool insecure)
    {
        File.WriteAllText("/workspace/earlier", ComparisonFixture.Reviewed);
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); info.ArgumentList.Add("--container"); info.ArgumentList.Add("late-child");
        using var child = Process.Start(info) ?? throw new InvalidOperationException("Child did not start");
        try
        {
            await Until(() => File.Exists("/workspace/ready"));
            var stopAck = DateTimeOffset.UtcNow;
            File.WriteAllText("/workspace/stop-ack", stopAck.ToString("O"));
            if (!insecure)
            {
                File.WriteAllText("/workspace/revoked", "revoked");
                child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            File.WriteAllText("/workspace/release", "release controlled late action");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var exitedAt = DateTimeOffset.UtcNow;
            var late = File.Exists("/workspace/late");
            var lateAt = late ? DateTimeOffset.Parse(File.ReadAllText("/workspace/late")) : (DateTimeOffset?)null;
            Require(late == insecure && (!late || lateAt >= stopAck), "late effect/stop ordering incorrect");
            Require(File.ReadAllText("/workspace/earlier") == ComparisonFixture.Reviewed, "earlier effect incorrectly undone");
            File.WriteAllText("/workspace/containment.json", JsonSerializer.Serialize(new { Insecure = insecure, StopAck = stopAck, KillSignal = !insecure,
                Revoked = File.Exists("/workspace/revoked"), ChildExited = child.HasExited, ExitedAt = exitedAt, LateWrite = late, LateAt = lateAt, EarlierWritePreserved = true }));
            Console.WriteLine(insecure ? "PASS unsafe: flag-only stop acknowledged, child wrote later, earlier effect preserved" : "PASS secure: revoked and owned child terminated, no late write, earlier effect preserved");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
    private static async Task Inspect()
    {
        using var client = Client("http://127.0.0.1:9000");
        Require(await client.GetStringAsync("/read") == Gateway.TaskDocument, "capture readiness");
        var counts = await client.GetFromJsonAsync<Dictionary<string, string[]>>("/effects");
        Require(counts!["unsafe"].SequenceEqual([ComparisonFixture.FakeSecret]) && counts["secure"].Length == 0, "actual capture effects differ from expected");
        File.WriteAllText("/tmp/capture-evidence.json", JsonSerializer.Serialize(counts));
        Console.WriteLine("PASS actual capture: unsafe=1 exact fake secret, secure=0");
    }
    private static async Task Until(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(10, deadline.Token); // Real IPC readiness, not an identity-expiry test.
    }
    private sealed record Capture(string Text);
}
