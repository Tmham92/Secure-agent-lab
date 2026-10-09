using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.AuditCollector;
using SecureAgentLab.Core;
using SecureAgentLab.Durable;
using SecureAgentLab.Transport;

if (args.Contains("--hang-fixture")) { Console.WriteLine("READY"); await Task.Delay(Timeout.Infinite); return 0; }
if (args.Contains("--phase7") || args.Contains("--drill")) return await Phase7Checks.Run(FindRoot());
using var rsa = RSA.Create(2048);
var privateKey = rsa.ExportPkcs8PrivateKeyPem();
var fixturesRoot = Path.Combine(FindRoot(), "artifacts", "durable-checks", Guid.NewGuid().ToString("N"));
var tests = new List<(string Name, Func<Task> Check)>();
var read = new Proposal(Operation.ReadDocument, "documents/task");
var draft = new Proposal(Operation.PublishReport, "reports/draft", 0, "Synthetic approved content.");
Fixture NewFixture() => new(fixturesRoot, privateKey);
void Add(string name, Action check) => tests.Add((name, () => { check(); return Task.CompletedTask; }));

Add("Sessions, budgets and approvals survive restart", () =>
{
    var f = NewFixture();
    var g = f.Open();
    var run = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    Equal(Outcome.Allowed, g.Execute(run, read).Outcome);
    g = f.Open();
    Equal(29, g.GetSession(run)!.RemainingCalls);
    Equal("publication_approved", g.Execute(run, draft, ticket, "once").Reason);
    g = f.Open();
    Equal(new MockEffects(1, 1), g.GetEffects());
    Equal("publication_replayed", g.Execute(run, draft, ticket, "once").Reason);
    Equal("approval_consumed", g.Execute(run, draft, ticket, "different").Reason);
    Equal(new MockEffects(1, 1), g.GetEffects());
});
Add("Exact content, destination, estimate and session binding", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g); var other = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    var preview = g.PreviewPublication(run, draft);
    Equal(draft.Content!, preview.Content);
    Equal(DurableGateway.ContentHash(draft), preview.ContentHash);
    Equal("approval_mismatch", g.Execute(run, draft with { Content = "Changed content" }, ticket, "key").Reason);
    Equal("out_of_scope", g.Execute(run, draft with { Resource = "reports/other" }, ticket, "key").Reason);
    Equal("approval_mismatch", g.Execute(run, draft with { EstimatedBytes = 1 }, ticket, "key").Reason);
    Equal("approval_mismatch", g.Execute(other, draft, ticket, "key").Reason);
    Equal("approval_unknown", g.Execute(run, draft, "forged", "key").Reason);
    Equal("idempotency_key_required", g.Execute(run, draft, ticket, null).Reason);
    Equal(new MockEffects(0, 0), g.GetEffects());
    Equal("publication_approved", g.Execute(run, draft, ticket, "key").Reason);
});
Add("Parallel redemption across gateway instances executes once", () =>
{
    var f = NewFixture(); var g = f.Open(); var other = f.Open(); var run = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    var results = new Decision[12];
    Parallel.For(0, results.Length, i => results[i] = (i % 2 == 0 ? g : other).Execute(run, draft, ticket, "key-" + i));
    Equal(1, results.Count(d => d.Outcome == Outcome.Allowed));
    Equal(11, results.Count(d => d.Reason == "approval_consumed"));
    Equal(new MockEffects(0, 1), f.Open().GetEffects());
});
Add("Parallel idempotent retries preserve one effect and charge responses", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    var results = new Decision[12];
    Parallel.For(0, results.Length, i => results[i] = g.Execute(run, draft, ticket, "same"));
    Equal(1, results.Count(d => d.Reason == "publication_approved"));
    Equal(11, results.Count(d => d.Reason == "publication_replayed"));
    Equal(18, g.GetSession(run)!.RemainingCalls);
    Equal(4096L - Encoding.UTF8.GetByteCount(Gateway.PublicationResult) * 12, g.GetSession(run)!.RemainingResponseBytes);
    Equal(new MockEffects(0, 1), g.GetEffects());
    var nextTicket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    Equal("idempotency_conflict", g.Execute(run, draft, nextTicket, "same").Reason);
});
Add("Policy version change invalidates outstanding grants and approvals", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    g = f.Open(version: "synthetic-v2");
    Equal("policy_version_changed", g.Execute(run, draft, ticket, "key").Reason);
    Equal(new MockEffects(0, 0), g.GetEffects());
    Throws<InvalidOperationException>(() => g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1)));
});
Add("Approval expiry and revoked runs remain blocked after restart", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromSeconds(1));
    f.Clock.Advance(TimeSpan.FromSeconds(1));
    g = f.Open();
    Equal("approval_expired", g.Execute(run, draft, ticket, "key").Reason);
    g.Revoke(run);
    Equal("identity_revoked", f.Open().Execute(run, draft, ticket, "key").Reason);
    Equal(new MockEffects(0, 0), g.GetEffects());
});
foreach (var point in Enum.GetValues<DurabilityPoint>())
    Add($"Publication recovers exactly once after {point}", () =>
    {
        var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
        var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
        var broken = f.Open(crash: observed => { if (observed == point) throw new SimulatedCrash(); });
        Throws<SimulatedCrash>(() => broken.Execute(run, draft, ticket, "once"));
        var recovered = f.Open();
        Equal(new MockEffects(0, 1), recovered.GetEffects());
        Equal("publication_replayed", recovered.Execute(run, draft, ticket, "once").Reason);
        Equal(new MockEffects(0, 1), recovered.GetEffects());
        Assert(!File.Exists(Path.Combine(f.State, "pending.json")), "Pending transaction not reconciled");
    });
Add("Audit collector recovers a complete signed suffix after head-write crash", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    f.Sink.Inner = new AuditCollectorStore(f.Log, f.Head, f.Stream, privateKey, _ => throw new SimulatedCrash());
    Throws<SimulatedCrash>(() => g.Execute(run, read));
    f.Sink.Inner = new AuditCollectorStore(f.Log, f.Head, f.Stream, privateKey);
    Equal(new MockEffects(1, 0), f.Open().GetEffects());
});
Add("Audit outage rejects writes before effects or approval consumption", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    f.Sink.Offline = true;
    Equal("authorization_dependency_unavailable", g.Execute(run, draft, ticket, "once").Reason);
    Throws<AuthorizationDependencyException>(() => g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1)));
    f.Sink.Offline = false;
    Equal(new MockEffects(0, 0), f.Open().GetEffects());
    Equal("publication_approved", g.Execute(run, draft, ticket, "once").Reason);
});
Add("Lost acknowledgement reports recovery required and reconciles once", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    f.Sink.LoseAcknowledgement = true;
    Equal(Outcome.RecoveryRequired, g.Execute(run, draft, ticket, "once").Outcome);
    f.Sink.LoseAcknowledgement = false;
    Equal(new MockEffects(0, 1), f.Open().GetEffects());
    Equal("publication_replayed", g.Execute(run, draft, ticket, "once").Reason);
});
Add("Stop and revoke markers contain work during sink outage", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    f.Sink.Offline = true;
    Throws<AuthorizationDependencyException>(() => g.Revoke(run));
    Equal("identity_revoked", g.Execute(run, read).Reason);
    Throws<AuthorizationDependencyException>(() => g.Stop());
    Equal("gateway_stopped", g.Execute(run, read).Reason);
    f.Sink.Offline = false;
    Equal("gateway_stopped", f.Open().Execute(run, read).Reason);
    var events = g.GetAuditSnapshot();
    Assert(events.Any(e => e.EventType == "run_revoked") && events.Any(e => e.EventType == "gateway_stopped"), "Outage containment was not reconciled to audit");
    Equal(new MockEffects(0, 0), g.GetEffects());
});
Add("Audit contains control events and redacts content, tickets and arbitrary identifiers", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
    var secretDraft = draft with { Content = "synthetic-secret-marker" };
    var ticket = g.ApprovePublication(run, secretDraft, TimeSpan.FromMinutes(1));
    g.Execute(run, new(Operation.ExternalRequest, "https://secret-marker.invalid/credential"));
    g.Execute(run, secretDraft, ticket, "idempotency-secret-marker");
    g.Revoke(run); g.Stop();
    var events = g.GetAuditSnapshot();
    foreach (var type in new[] { "grant_issued", "approval_issued", "execution", "run_revoked", "gateway_stopped" })
        Assert(events.Any(e => e.EventType == type), "Control event missing: " + type);
    var auditText = File.ReadAllText(Path.Combine(f.Log, "audit.jsonl"));
    var stateText = File.ReadAllText(Path.Combine(f.State, "state.json"));
    foreach (var secret in new[] { secretDraft.Content!, ticket, "idempotency-secret-marker", "https://secret-marker.invalid/credential" })
        Assert(!auditText.Contains(secret) && !stateText.Contains(secret), "Sensitive input persisted");
    Assert(AuditChain.VerifyAudit(events), "Audit invalid");
});
Add("Tail truncation is detected using independent signed head", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g); g.Execute(run, read);
    var path = Path.Combine(f.Log, "audit.jsonl");
    var lines = File.ReadAllLines(path);
    File.WriteAllText(path, string.Join('\n', lines.Take(lines.Length - 1)) + "\n");
    Throws<AuthorizationDependencyException>(() => f.Open());
});
Add("Rehashed audit history cannot forge checkpoint signatures", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g); g.Execute(run, read);
    var path = Path.Combine(f.Log, "audit.jsonl");
    var events = f.Sink.GetEvents().ToArray();
    var entry = events[^1].Entry with { Reason = "rewritten" };
    entry = entry with { Hash = AuditChain.ComputeHash(entry) };
    events[^1] = new(entry, events[^1].Checkpoint with { Value = events[^1].Checkpoint.Value with { Hash = entry.Hash } });
    File.WriteAllText(path, string.Join('\n', events.Select(e => JsonSerializer.Serialize(e))) + "\n");
    Throws<AuthorizationDependencyException>(() => f.Open());
});
Add("State alteration, state rollback and missing state fail closed", () =>
{
    foreach (var mode in new[] { "tamper", "rollback", "delete" })
    {
        var f = NewFixture(); var g = f.Open(); var run = f.Run(g);
        var path = Path.Combine(f.State, "state.json");
        var previous = File.ReadAllBytes(path);
        g.Execute(run, read);
        if (mode == "delete") File.Delete(path);
        else if (mode == "rollback") File.WriteAllBytes(path, previous);
        else
        {
            var state = DurableFiles.Read<DurableSnapshot>(path);
            state.Domain.Reads++;
            DurableFiles.Write(path, state);
        }
        Throws<AuthorizationDependencyException>(() => f.Open());
    }
});
Add("Unsigned prepared-state mutation cannot authorize a publication", () =>
{
    var f = NewFixture(); var g = f.Open(); var run = f.Run(g); var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    var broken = f.Open(crash: point => { if (point == DurabilityPoint.AfterPrepared) throw new SimulatedCrash(); });
    Throws<SimulatedCrash>(() => broken.Execute(run, draft, ticket, "once"));
    var path = Path.Combine(f.State, "pending.json");
    var pending = DurableFiles.Read<SealedChange>(path);
    pending.Change.Domain.Reads = 900;
    DurableFiles.Write(path, pending);
    Throws<AuthorizationDependencyException>(() => f.Open());
});
Add("Clock regression and byte-limit failures never publish", () =>
{
    var f = NewFixture(); var g = f.Open();
    var run = g.CreateSession(TaskGrant.Default(f.Clock.GetUtcNow().AddMinutes(5), 10, 0));
    var ticket = g.ApprovePublication(run, draft, TimeSpan.FromMinutes(1));
    Equal("response_budget_exhausted", g.Execute(run, draft, ticket, "once").Reason);
    f.Clock.Advance(TimeSpan.FromSeconds(-1));
    Equal("authorization_dependency_unavailable", g.Execute(run, draft, ticket, "once").Reason);
    f.Clock.Advance(TimeSpan.FromSeconds(1));
    Equal(new MockEffects(0, 0), g.GetEffects());
});

// Real HTTP collector + gateway, followed by reconstructing the gateway against the same durable state.
tests.Add(("HTTP collector and gateway preserve credentials, approvals and effects across restart", async () =>
{
    var f = NewFixture();
    var writeKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    await using var collectorApp = CollectorApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Error"],
        new(f.Log, f.Head, f.Stream, privateKey, writeKey, true));
    await collectorApp.StartAsync();
    var collectorAddress = Address(collectorApp.Services);
    using var sink = new HttpAuditCollector(collectorAddress, writeKey, true);
    using var unauthenticated = new HttpClient { BaseAddress = new Uri(collectorAddress) };
    using var forbidden = await unauthenticated.GetAsync("/checkpoint");
    Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
    var settings = new CredentialSettings("issuer", "audience", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    var issuer = new CredentialService(settings, f.Clock);
    var token = issuer.Issue("operator", "operator", TimeSpan.FromMinutes(5));
    var durable = new DurableGateway(f.State, sink, rsa.ExportSubjectPublicKeyInfoPem(), f.Stream, f.IntegrityKey, clock: f.Clock);
    IssuedRun run; string approval;
    await using (var app = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Error"], settings, f.Clock, durable, true))
    {
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Address(app.Services)) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var created = await client.PostAsJsonAsync("/operator/runs", new IssueRunRequest());
        created.EnsureSuccessStatusCode(); run = (await created.Content.ReadFromJsonAsync<IssuedRun>())!;
        using var preview = await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approval-preview", new ApprovalRequest(draft));
        preview.EnsureSuccessStatusCode();
        Equal(draft.Content!, (await preview.Content.ReadFromJsonAsync<PublicationPreview>())!.Content);
        using var approved = await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approvals", new ApprovalRequest(draft));
        approved.EnsureSuccessStatusCode(); approval = (await approved.Content.ReadFromJsonAsync<IssuedApproval>())!.Ticket;
        client.DefaultRequestHeaders.Authorization = new("Bearer", run.WorkerCredential);
        using var denied = await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approval-preview", new ApprovalRequest(draft));
        Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await app.StopAsync();
    }
    durable = new DurableGateway(f.State, sink, rsa.ExportSubjectPublicKeyInfoPem(), f.Stream, f.IntegrityKey, clock: f.Clock);
    await using (var app = LabApi.Build(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Error"], settings, f.Clock, durable, true))
    {
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(Address(app.Services)) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", run.WorkerCredential);
        using var response = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(draft, approval, "http-once"));
        response.EnsureSuccessStatusCode();
        Equal("publication_approved", (await response.Content.ReadFromJsonAsync<Decision>())!.Reason);
        using var replay = await client.PostAsJsonAsync("/worker/proposals", new ExecuteRequest(draft, approval, "http-once"));
        Equal("publication_replayed", (await replay.Content.ReadFromJsonAsync<Decision>())!.Reason);
        Equal(new MockEffects(0, 1), durable.GetEffects());
        await app.StopAsync();
    }
    await collectorApp.StopAsync();
    var failed = durable.Execute(run.RunId, draft, approval, "new-key");
    Equal("authorization_dependency_unavailable", failed.Reason);
}));

var failures = 0;
foreach (var (name, check) in tests)
{
    try { await check(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} durable checks passed.");
return failures == 0 ? 0 : 1;

static string Address(IServiceProvider services) => services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "SecureAgentLab.slnx"))) d = d.Parent;
    return d?.FullName ?? throw new InvalidOperationException("Repository missing");
}
static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}");
}
sealed class SimulatedCrash : Exception;
sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now = now.Add(duration);
}
sealed class SwitchableCollector(IAuditCollector inner) : IAuditCollector
{
    public IAuditCollector Inner { get; set; } = inner;
    public bool Offline { get; set; }
    public bool LoseAcknowledgement { get; set; }
    private void Check() { if (Offline) throw new AuthorizationDependencyException("sink_offline"); }
    public SignedCheckpoint GetCheckpoint() { Check(); return Inner.GetCheckpoint(); }
    public IReadOnlyList<SignedAuditEvent> GetEvents() { Check(); return Inner.GetEvents(); }
    public SignedCheckpoint Append(AuditEntry e)
    {
        Check(); var result = Inner.Append(e);
        if (LoseAcknowledgement) throw new AuthorizationDependencyException("ack_lost");
        return result;
    }
}
sealed class Fixture
{
    public string State { get; }
    public string Log { get; }
    public string Head { get; }
    public string Stream { get; } = Guid.NewGuid().ToString("N");
    public byte[] IntegrityKey { get; } = RandomNumberGenerator.GetBytes(32);
    public TestClock Clock { get; } = new();
    public SwitchableCollector Sink { get; }
    private readonly string publicKey;
    public Fixture(string root, string privateKey)
    {
        root = Path.Combine(root, Guid.NewGuid().ToString("N"));
        State = Path.Combine(root, "gateway"); Log = Path.Combine(root, "collector-log"); Head = Path.Combine(root, "independent-head");
        var collector = new AuditCollectorStore(Log, Head, Stream, privateKey);
        publicKey = collector.PublicKey;
        Sink = new(collector);
    }
    public DurableGateway Open(string version = "synthetic-v1", Action<DurabilityPoint>? crash = null) =>
        new(State, Sink, publicKey, Stream, IntegrityKey, version, Clock, crash);
    public string Run(DurableGateway g) => g.CreateSession(TaskGrant.Default(Clock.GetUtcNow().AddMinutes(5), 30, 4096));
}
