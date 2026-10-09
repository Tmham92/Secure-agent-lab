using System.Text;
using SecureAgentLab.Core;

var checks = new (string Name, Action Run)[]
{
    ("Unknown identity cannot execute", () =>
    {
        var g = new Gateway();
        Expect(g.Execute("forged", Read()), Outcome.Denied, "unknown_identity");
        Expect(g.Execute(null, Read()), Outcome.Denied, "unknown_identity");
        Effects(g, 0, 0);
        Equal(2, g.GetAuditSnapshot().Count);
    }),
    ("Expiry at exact deadline and revocation", () =>
    {
        var clock = new FakeClock();
        var g = new Gateway(clock);
        var run = Session(g);
        clock.Advance(TimeSpan.FromMinutes(5));
        Expect(g.Execute(run, Read()), Outcome.Denied, "identity_expired");
        var other = Session(g);
        var ticket = Approve(g, other);
        g.Revoke(other);
        Expect(g.Execute(other, Read()), Outcome.Denied, "identity_revoked");
        Expect(g.Execute(other, Publish(), ticket), Outcome.Denied, "identity_revoked");
        Throws<InvalidOperationException>(() => Approve(g, other));
        Effects(g, 0, 0);
    }),
    ("Exact read measures UTF-8 content", () =>
    {
        var g = new Gateway();
        var bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
        var run = Session(g, bytes: bytes);
        var d = g.Execute(run, Read());
        Expect(d, Outcome.Allowed, "scoped_read");
        Equal(Gateway.TaskDocument, d.Result);
        Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        Effects(g, 1, 0);
    }),
    ("Scope rejects traversal and all unsupported operations", () =>
    {
        var g = new Gateway();
        var run = Session(g);
        foreach (var resource in new[] { "documents/../secrets", "documents/task/", "Documents/task", "documents/other", " documents/task", "documents/task\0" })
            Expect(g.Execute(run, Read(resource)), Outcome.Denied, "out_of_scope");
        foreach (var op in new[] { Operation.ExternalRequest, Operation.MessageAgent, Operation.ChangePermissions })
            Expect(g.Execute(run, new(op, "documents/task")), Outcome.Denied, "out_of_scope");
        Expect(g.Execute(run, Publish("reports/other")), Outcome.Denied, "out_of_scope");
        Effects(g, 0, 0);
    }),
    ("Malformed proposals fail closed and consume calls", () =>
    {
        var g = new Gateway();
        var run = Session(g, calls: 6);
        Proposal?[] proposals = [null, new((Operation)999, "documents/task"), new(Operation.ReadDocument, null!),
            new(Operation.ReadDocument, ""), new(Operation.ReadDocument, "  "), new(Operation.ReadDocument, "documents/task", -1)];
        foreach (var p in proposals) Expect(g.Execute(run, p), Outcome.Denied, "malformed_proposal");
        Expect(g.Execute(run, Read()), Outcome.Denied, "call_budget_exhausted");
        Effects(g, 0, 0);
    }),
    ("Publication requires host approval; forged and replayed tickets denied", () =>
    {
        var g = new Gateway();
        var run = Session(g);
        Expect(g.Execute(run, Publish()), Outcome.ApprovalRequired, "publication_approval_required");
        Expect(g.Execute(run, Publish(), "forged"), Outcome.Denied, "approval_unknown");
        Effects(g, 0, 0);
        var ticket = Approve(g, run);
        Expect(g.Execute(run, Publish(), ticket), Outcome.Allowed, "publication_approved");
        Expect(g.Execute(run, Publish(), ticket), Outcome.Denied, "approval_consumed");
        Effects(g, 0, 1);
    }),
    ("Approval expires at deadline", () =>
    {
        var clock = new FakeClock();
        var g = new Gateway(clock);
        var run = Session(g);
        var ticket = Approve(g, run);
        clock.Advance(TimeSpan.FromMinutes(1));
        Expect(g.Execute(run, Publish(), ticket), Outcome.Denied, "approval_expired");
        Effects(g, 0, 0);
    }),
    ("Approval cannot cross sessions or altered proposals", () =>
    {
        var g = new Gateway();
        var run = Session(g);
        var other = Session(g);
        var ticket = Approve(g, run);
        Expect(g.Execute(other, Publish(), ticket), Outcome.Denied, "approval_mismatch");
        Expect(g.Execute(run, Publish(estimate: 1), ticket), Outcome.Denied, "approval_mismatch");
        Expect(g.Execute(run, Publish("reports/other"), ticket), Outcome.Denied, "out_of_scope");
        Expect(g.Execute(run, Read(), ticket), Outcome.Allowed, "scoped_read");
        Effects(g, 1, 0);
        // Invalid redemption attempts did not consume the rightful approval.
        Expect(g.Execute(run, Publish(), ticket), Outcome.Allowed, "publication_approved");
        Effects(g, 1, 1);
        Throws<InvalidOperationException>(() => g.ApprovePublication(run, Publish("reports/other"), TimeSpan.FromMinutes(1)));
        Throws<InvalidOperationException>(() => g.ApprovePublication(run, Read(), TimeSpan.FromMinutes(1)));
    }),
    ("Actual byte limits defeat zero estimates and prevent effects", () =>
    {
        var g = new Gateway();
        var run = Session(g, bytes: Encoding.UTF8.GetByteCount(Gateway.TaskDocument) - 1);
        Expect(g.Execute(run, Read(estimate: 0)), Outcome.Denied, "response_budget_exhausted");
        var publishRun = Session(g, bytes: Encoding.UTF8.GetByteCount(Gateway.PublicationResult) - 1);
        Expect(g.Execute(publishRun, Publish(), Approve(g, publishRun)), Outcome.Denied, "response_budget_exhausted");
        Effects(g, 0, 0);
    }),
    ("Cumulative response quota and publication accounting", () =>
    {
        var g = new Gateway();
        var bytes = Encoding.UTF8.GetByteCount(Gateway.PublicationResult);
        var run = Session(g, bytes: bytes);
        Expect(g.Execute(run, Publish(), Approve(g, run)), Outcome.Allowed, "publication_approved");
        Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        Expect(g.Execute(run, Publish(), Approve(g, run)), Outcome.Denied, "response_budget_exhausted");
        Effects(g, 0, 1);
    }),
    ("Denied attempts exhaust call budget", () =>
    {
        var g = new Gateway();
        var run = Session(g, calls: 2);
        Expect(g.Execute(run, Read("secrets")), Outcome.Denied, "out_of_scope");
        Expect(g.Execute(run, Publish()), Outcome.ApprovalRequired, "publication_approval_required");
        Expect(g.Execute(run, Read()), Outcome.Denied, "call_budget_exhausted");
        Effects(g, 0, 0);
    }),
    ("Concurrent requests cannot overspend call quota", () =>
    {
        var g = new Gateway();
        var run = Session(g, calls: 7);
        var results = new Decision[100];
        Parallel.For(0, results.Length, i => results[i] = g.Execute(run, Read()));
        Equal(7, results.Count(d => d.Outcome == Outcome.Allowed));
        Equal(93, results.Count(d => d.Reason == "call_budget_exhausted"));
        Effects(g, 7, 0);
        Equal(100, g.GetAuditSnapshot().Count);
        Assert(AuditChain.VerifyAudit(g.GetAuditSnapshot()), "Concurrent audit invalid");
    }),
    ("Concurrent requests cannot overspend byte quota", () =>
    {
        var g = new Gateway();
        var bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
        var run = Session(g, calls: 100, bytes: bytes * 3L);
        var results = new Decision[100];
        Parallel.For(0, results.Length, i => results[i] = g.Execute(run, Read(estimate: 0)));
        Equal(3, results.Count(d => d.Outcome == Outcome.Allowed));
        Equal(97, results.Count(d => d.Reason == "response_budget_exhausted"));
        Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
        Effects(g, 3, 0);
    }),
    ("Concurrent approval redemption executes once", () =>
    {
        var g = new Gateway();
        var run = Session(g, calls: 100);
        var ticket = Approve(g, run);
        var results = new Decision[100];
        Parallel.For(0, results.Length, i => results[i] = g.Execute(run, Publish(), ticket));
        Equal(1, results.Count(d => d.Outcome == Outcome.Allowed));
        Equal(99, results.Count(d => d.Reason == "approval_consumed"));
        Effects(g, 0, 1);
    }),
    ("Stop blocks execution and further host issuance", () =>
    {
        var g = new Gateway();
        var run = Session(g);
        var ticket = Approve(g, run);
        g.Stop();
        g.Stop();
        Expect(g.Execute(run, Publish(), ticket), Outcome.Denied, "gateway_stopped");
        Expect(g.Execute(run, Read()), Outcome.Denied, "gateway_stopped");
        Throws<InvalidOperationException>(() => Session(g));
        Throws<InvalidOperationException>(() => Approve(g, run));
        Effects(g, 0, 0);
    }),
    ("Invalid host configuration rejected; zero quotas valid", () =>
    {
        var g = new Gateway();
        Throws<ArgumentOutOfRangeException>(() => g.CreateSession(TimeSpan.Zero, 1, 1));
        Throws<ArgumentOutOfRangeException>(() => Session(g, calls: -1));
        Throws<ArgumentOutOfRangeException>(() => Session(g, bytes: -1));
        var run = Session(g, calls: 0, bytes: 0);
        Expect(g.Execute(run, Read()), Outcome.Denied, "call_budget_exhausted");
        Throws<ArgumentOutOfRangeException>(() => g.ApprovePublication(run, Publish(), TimeSpan.Zero));
    }),
    ("Audit mutation, reorder, removal and snapshot isolation", () =>
    {
        var g = new Gateway(new FakeClock());
        var run = Session(g);
        g.Execute(run, Read());
        var old = g.GetAuditSnapshot();
        g.Execute(run, Publish());
        Equal(1, old.Count);
        var entries = g.GetAuditSnapshot().ToArray();
        Assert(AuditChain.VerifyAudit(entries), "Original chain invalid");
        var e = entries[0];
        AuditEntry[] mutations = [e with { Reason = "tampered" }, e with { Sequence = 2 },
            e with { UtcTime = e.UtcTime.AddSeconds(1) }, e with { RunId = "forged" },
            e with { Proposal = Read("secrets") }, e with { Outcome = Outcome.Denied },
            e with { PreviousHash = "fake" }, e with { Hash = "fake" }];
        foreach (var mutation in mutations)
            Assert(!AuditChain.VerifyAudit(new[] { mutation, entries[1] }), "Mutation undetected");
        Assert(!AuditChain.VerifyAudit(entries.Reverse()), "Reorder undetected");
        Assert(!AuditChain.VerifyAudit(entries.Skip(1)), "Missing head undetected");
        Assert(AuditChain.VerifyAudit(entries.Take(1)), "Valid prefix should pass; no trusted checkpoint");
        Assert(AuditChain.VerifyAudit([]), "Empty prefix should pass; no trusted checkpoint");
        // Changing a copied array cannot alter the gateway-owned records.
        entries[0] = mutations[0];
        Assert(AuditChain.VerifyAudit(g.GetAuditSnapshot()), "Snapshot leaked mutable state");
        Assert(old is IList<AuditEntry> list && list.IsReadOnly, "Snapshot collection is writable");
    })
};

var failures = 0;
foreach (var (name, run) in checks)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static Proposal Read(string resource = "documents/task", long? estimate = null) => new(Operation.ReadDocument, resource, estimate);
static Proposal Publish(string resource = "reports/draft", long? estimate = 0) => new(Operation.PublishReport, resource, estimate);
static string Session(Gateway g, int calls = 100, long bytes = 100_000) => g.CreateSession(TimeSpan.FromMinutes(5), calls, bytes);
static string Approve(Gateway g, string run) => g.ApprovePublication(run, Publish(), TimeSpan.FromMinutes(1));
static void Expect(Decision d, Outcome outcome, string reason)
{
    Equal(outcome, d.Outcome);
    Equal(reason, d.Reason);
    if (outcome != Outcome.Allowed) Equal<string?>(null, d.Result);
}
static void Effects(Gateway g, int reads, int publications) => Equal(new MockEffects(reads, publications), g.GetEffects());
static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}");
}

sealed class FakeClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now = now.Add(duration);
}
