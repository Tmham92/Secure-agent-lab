using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SecureAgentLab.Playground.Advanced;
using SecureAgentLab.Playground.Lab;

var checks = new List<(string Name, Func<Task> Run)>();
void Check(string name, Action action) => checks.Add((name, () => { action(); return Task.CompletedTask; }));
void Require(bool condition) { if (!condition) { throw new InvalidOperationException("Security assertion failed."); } }
LabRun New(bool hardened = true, TimeProvider? clock = null, int limit = 6) => new(hardened, Scenarios.Ticket, clock ?? TimeProvider.System, limit);
Check("Allowed read and fixed summary output", () =>
{
    using var run = New();
    Require(run.Execute(new("read_file", Path: "documents/ticket.txt")).Result == Scenarios.Ticket);
    Require(run.Execute(new("write_summary", Text: "Summary")).Allowed && run.Summary == "Summary");
    Require(!run.Execute(new("write_summary", Path: "../elsewhere", Text: "bad")).Allowed && run.Summary == "Summary");
});
Check("Traversal, absolute paths, prefix confusion and unknown tools have no effects", () =>
{
    foreach (string path in new[] { "../private/secret.txt", "private/secret.txt", "C:/Windows/win.ini", "/etc/passwd", "documents-other/ticket.txt", "documents/../private/secret.txt", "documents\\ticket.txt" })
    {
        using var run = New();
        ToolDecision result = run.Execute(new("read_file", Path: path));
        Require(!result.Allowed && result.Result is null && run.Outbox.Length == 0 && run.Summary == "");
    }
    using var unknown = New();
    Require(!unknown.Execute(new("shell", Text: "anything")).Allowed);
});
Check("Denied send does not mutate outbox", () =>
{
    using var run = New();
    Require(!run.Execute(new("send_message", Recipient: "attacker@example.test", Text: LabRun.FakeSecret)).Allowed);
    Require(run.Outbox.Length == 0 && !run.Leaked && run.Pending is null);
});
Check("Forged approval rejected; reviewed message executes exactly once", () =>
{
    using var run = New(limit: 16);
    var call = new ToolCall("send_message", Recipient: "support@example.test", Text: "reviewed");
    Require(!run.Execute(call with
    {
        Approved = true
    }).Allowed && run.Pending is null);
    Require(!run.Execute(call).Allowed && run.Outbox.Length == 0);
    var pending = run.Pending!;
    Require(run.Approve(pending));
    Require(run.Execute(call).Allowed);
    Require(!run.Execute(call).Allowed && run.Outbox.Length == 1);
});
Check("Altered message cannot use approval", () =>
{
    using var run = New();
    var call = new ToolCall("send_message", Recipient: "support@example.test", Text: "reviewed");
    run.Execute(call);
    Require(run.Approve(run.Pending!));
    Require(!run.Execute(call with
    {
        Text = "substituted"
    }).Allowed && run.Outbox.Length == 0);
});
Check("Approval cannot cross runs", () =>
{
    using var first = New();
    using var second = New();
    var call = new ToolCall("send_message", Recipient: "support@example.test", Text: "reviewed");
    first.Execute(call);
    second.Execute(call);
    Require(!second.Approve(first.Pending!));
    Require(!second.Execute(call).Allowed && first.Outbox.Length == 0 && second.Outbox.Length == 0);
});
Check("Expiry and deadline enforced at exact boundary", () =>
{
    var clock = new LabClock();
    using var run = New(clock: clock);
    var call = new ToolCall("send_message", Recipient: "support@example.test", Text: "reviewed");
    run.Execute(call);
    Require(run.Approve(run.Pending!));
    clock.Advance(TimeSpan.FromSeconds(30));
    Require(!run.Execute(call).Allowed && run.Outbox.Length == 0);
    clock.Advance(TimeSpan.FromSeconds(30));
    Require(!run.Execute(new("write_summary", Text: "late")).Allowed && run.Summary == "");
});
Check("Concurrent approval redemption executes once", () =>
{
    using var run = New(limit: 16);
    var call = new ToolCall("send_message", Recipient: "support@example.test", Text: "reviewed");
    run.Execute(call);
    Require(run.Approve(run.Pending!));
    int admitted = 0;
    Parallel.For(0, 8, _ => { if (run.Execute(call).Allowed) { Interlocked.Increment(ref admitted); } });
    Require(admitted == 1 && run.Outbox.Length == 1);
});
Check("Denied calls consume budget and stopped runs cannot execute", () =>
{
    using var run = New(limit: 2);
    run.Execute(new("unknown"));
    run.Execute(new("unknown"));
    Require(!run.Execute(new("write_summary", Text: "late")).Allowed && run.Calls == 2 && run.Stopped && run.Summary == "");
});
Check("Cancellation prevents effects", () =>
{
    using var run = New();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        run.Execute(new("write_summary", Text: "cancelled"), cancellation.Token);
        throw new InvalidOperationException("Cancellation not observed.");
    }
    catch (OperationCanceledException) { Require(run.Summary == "" && run.Calls == 0); }
});
checks.Add(("All scripted scenarios: baseline failures and hardened positive controls", async () =>
{
    foreach (Scenario scenario in Scenarios.All)
    {
        using var session = new LabSession(TimeProvider.System, new OfflineModel());
        await session.RunAsync(scenario, scenario.Document, false, false);
        await session.RunAsync(scenario, scenario.Document, true, false);
        LabRun baseline = session.Runs[0], hardened = session.Runs[1];
        Require(baseline.Id != hardened.Id && baseline.Summary.Length > 0);
        Require(!hardened.Leaked && hardened.Outbox.Length == 0);
        if (scenario.Id != "loop")
        {
            Require(hardened.Summary.Length > 0);
        }
        if (scenario.Id == "injection")
        {
            Require(baseline.Leaked);
        }
        if (scenario.Id is "exfiltration" or "approval")
        {
            Require(baseline.Outbox.Length == 1);
        }
        if (scenario.Id == "files")
        {
            Require(baseline.Trace.Any(t => t.Decision.Result == LabRun.FakeSecret));
        }
        if (scenario.Id == "loop")
        {
            Require(hardened.Stopped && hardened.Summary == "");
        }
    }
}
));
checks.Add(("Session approval workflow and reset isolation", async () =>
{
    using var first = new LabSession(TimeProvider.System, new OfflineModel());
    using var second = new LabSession(TimeProvider.System, new OfflineModel());
    Scenario scenario = Scenarios.All.Single(x => x.Id == "approval");
    await first.RunAsync(scenario, scenario.Document, true, false);
    await second.RunAsync(scenario, scenario.Document, true, false);
    LabRun run = first.Runs[0];
    Require(first.ApproveAndSend(run, run.Pending!) && run.Outbox.Length == 1);
    first.Reset();
    Require(first.Runs.Length == 0 && second.Runs.Length == 1 && second.Runs[0].Outbox.Length == 0);
}
));
checks.Add(("Session cancel and overlapping run protection", async () =>
{
    using var session = new LabSession(TimeProvider.System, new OfflineModel());
    Scenario scenario = Scenarios.All.Single(x => x.Id == "loop");
    Task pending = session.RunAsync(scenario, scenario.Document, false, false);
    await session.RunAsync(scenario, scenario.Document, true, false);
    session.Cancel();
    await pending;
    Require(session.Runs.Length == 1 && !session.Busy && session.Runs[0].Status == "Cancelled or deadline expired");
}
));
Check("Live parser rejects duplicates, incomplete output and malformed proposals", () =>
{
    foreach (string body in new[] { "{}", "{\"status\":\"incomplete\",\"output\":[]}", "{\"status\":\"completed\",\"status\":\"completed\",\"output\":[]}" })
    {
        bool rejected = false;
        try
        {
            OpenAiLabModel.Parse(Encoding.UTF8.GetBytes(body));
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or JsonException) { rejected = true; }
        Require(rejected);
    }
});
checks.Add(("Live adapter uses fixed endpoint and feeds bounded proposals to common dispatcher (mock HTTP)", async () =>
{
    var handler = new ModelHandler();
    using var client = new HttpClient(handler);
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OPENAI_API_KEY"] = "fake-key", ["LAB_OPENAI_MODEL"] = "configured-test-model" }).Build();
    var model = new OpenAiLabModel(client, configuration);
    using var session = new LabSession(TimeProvider.System, model);
    await session.RunAsync(Scenarios.All[0], Scenarios.Ticket, true, true);
    Require(handler.Calls == 1 && session.Runs[0].Summary == "Synthetic live summary");
}
));
Check("Live proposals require every field and reject extra authority or null arguments", () =>
{
    foreach (string proposal in new[]
    {
        "{\"calls\":[{\"tool\":\"write_summary\"}]}",
        "{\"calls\":[{\"tool\":\"write_summary\",\"path\":\"\",\"recipient\":\"\",\"text\":null,\"approved\":false}]}",
        "{\"calls\":[{\"tool\":\"write_summary\",\"path\":\"\",\"recipient\":\"\",\"text\":\"x\",\"approved\":false,\"authority\":\"admin\"}]}",
        "{\"calls\":[],\"calls\":[]}",
        "{\"calls\":[]}"
    })
    {
        string envelope = JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = proposal } } } }
        });
        bool rejected = false;
        try
        {
            OpenAiLabModel.Parse(Encoding.UTF8.GetBytes(envelope));
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException) { rejected = true; }
        Require(rejected);
    }
});
checks.Add(("Invalid settings do not leave session busy; oversized tool output has no effects", async () =>
{
    using var session = new LabSession(TimeProvider.System, new OfflineModel());
    try
    {
        await session.RunAsync(Scenarios.All[0], Scenarios.Ticket, true, false, 99);
        throw new InvalidOperationException("Invalid limit accepted.");
    }
    catch (ArgumentException) { Require(!session.Busy && session.Runs.Length == 0); }
    using var run = New();
    Require(!run.Execute(new("write_summary", Text: new string('x', 8001))).Allowed && run.Summary == "");
}
));
Check("Advanced catalog exposes ten comparisons, two suites and two Docker guides", () =>
{
    Require(AdvancedCatalog.All.Count == 14 && AdvancedCatalog.All.Select(s => s.Id).Distinct().Count() == 14);
    Require(AdvancedCatalog.All.Count(s => s.Category == "Baseline vs hardened") == 10);
    Require(AdvancedCatalog.All.Count(s => s.Project is null) == 2);
    try
    {
        AdvancedCatalog.Find("audit; shell");
        throw new InvalidOperationException("Unknown scenario accepted.");
    }
    catch (ArgumentException) { }
});
checks.Add(("Advanced runner fails closed for unknown scenarios and missing builds", async () =>
{
    var runner = new AdvancedProcessRunner(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "Release");
    Require((await runner.RunAsync("audit")).Status == "Build required");
    Require((await runner.RunAsync("isolation")).Status == "Run with Docker launcher");
    try
    {
        await runner.RunAsync("../audit");
        throw new InvalidOperationException("Caller path accepted.");
    }
    catch (ArgumentException) { }
}
));
checks.Add(("All ten advanced comparisons return real structured positive-control evidence", async () =>
{
    string repo = AdvancedProcessRunner.FindRepository(AppContext.BaseDirectory);
    var runner = new AdvancedProcessRunner(repo, "Release");
    foreach (AdvancedScenario scenario in AdvancedCatalog.All.Where(s => s.Category == "Baseline vs hardened"))
    {
        AdvancedResult result = await runner.RunAsync(scenario.Id);
        Require(result.Status == "Passed" && result.ExitCode == 0 && result.Comparison is { Passed: true, Unsafe.PositiveControl: true, Secure.PositiveControl: true });
        Require(result.Comparison!.InputSha256.Length == 64 && result.Output.Contains("PASS", StringComparison.Ordinal));
        Require(File.Exists(Path.Combine(repo, "artifacts", scenario.EvidenceCase!, "latest", "comparisons.json")));
    }
}
));
checks.Add(("Advanced durable suite returns actual successful check output", async () =>
{
    var runner = new AdvancedProcessRunner(AdvancedProcessRunner.FindRepository(AppContext.BaseDirectory), "Release");
    AdvancedResult result = await runner.RunAsync("durable");
    Require(result.Status == "Passed" && result.Comparison is null && result.Output.Contains("20/20 durable checks passed", StringComparison.Ordinal));
}
));
checks.Add(("Advanced collaboration suite returns actual successful check output", async () =>
{
    var runner = new AdvancedProcessRunner(AdvancedProcessRunner.FindRepository(AppContext.BaseDirectory), "Release");
    AdvancedResult result = await runner.RunAsync("collaboration");
    Require(result.Status == "Passed" && result.Comparison is null && result.Output.Contains("18/18 checks passed", StringComparison.Ordinal));
}
));
checks.Add(("Advanced cancellation releases global admission and overlapping runs are rejected", async () =>
{
    var runner = new AdvancedProcessRunner(AdvancedProcessRunner.FindRepository(AppContext.BaseDirectory), "Release");
    using var cancellation = new CancellationTokenSource();
    Task<AdvancedResult> active = runner.RunAsync("durable", cancellation.Token);
    AdvancedResult other = await runner.RunAsync("audit");
    Require(other.Status == "Runner busy");
    cancellation.Cancel();
    AdvancedResult stopped = await active;
    Require(stopped.Status == "Cancelled or timed out");
    Require((await runner.RunAsync("audit")).Status == "Passed");
}
));
int failures = 0;
foreach (var check in checks)
{
    try
    {
        await check.Run();
        Console.WriteLine("PASS " + check.Name);
    }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + check.Name + ": " + e.Message); }
}
Console.WriteLine($"{checks.Count - failures}/{checks.Count} playground checks passed.");
return failures == 0 ? 0 : 1;

sealed class OfflineModel : ILabModel
{
    public bool Configured => false;
    public Task<ToolCall[]> ProposeAsync(string document, TraceEntry[] history, CancellationToken cancellationToken) => throw new InvalidOperationException("No network in scripted tests.");
}
sealed class LabClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan value) => now += value;
}
sealed class ModelHandler : HttpMessageHandler
{
    public int Calls
    {
        get; private set;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsoluteUri != "https://api.openai.com/v1/responses")
        {
            throw new InvalidOperationException("Wrong endpoint.");
        }
        string requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (!requestBody.Contains("untrustedDocument", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Missing document.");
        }
        Calls++;
        string text = JsonSerializer.Serialize(new
        {
            calls = new[] { new { tool = "write_summary", path = "", recipient = "", text = "Synthetic live summary", approved = false } }
        });
        string body = JsonSerializer.Serialize(new
        {
            status = "completed",
            output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text } } } }
        });
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        };
    }
}



