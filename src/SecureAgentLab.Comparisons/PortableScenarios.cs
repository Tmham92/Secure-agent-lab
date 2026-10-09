using System.Security.Cryptography;
using System.Text;
using SecureAgentLab.Core;
using SecureAgentLab.Durable;
using static SecureAgentLab.Comparisons.ComparisonEvidence;

namespace SecureAgentLab.Comparisons;

public static class PortableScenarios
{
    public static readonly string[] Names = ["approval", "scope", "actions", "files", "injection", "retry", "audit", "version", "quota", "retry-limit"];
    public static Comparison Run(string id, string root) => id switch
    {
        "approval" => Approval(root), "scope" => Scope(root), "actions" => Actions(root), "files" => Files(root),
        "injection" => Injection(root), "retry" => Retry(root), "audit" => Audit(root), "version" => Version(root),
        "quota" => Quota(root), "retry-limit" => RetryLimit(root), _ => throw new ArgumentException("Unknown comparison scenario")
    };
    private static (ComparisonFixture Unsafe, ComparisonFixture Secure) Fixtures(string root, string id) =>
        (new(root, id, "unsafe"), new(root, id, "secure"));
    private static Proposal Draft(string content = ComparisonFixture.Reviewed) => new(Operation.PublishReport, "reports/draft", Content: content, ExpectedVersion: 0);
    private static bool Positive(Gateway gateway, string run) => gateway.Execute(run, new(Operation.ReadDocument, "documents/task")).Result == Gateway.TaskDocument;
    private static bool Positive(DurableGateway gateway, string run) => gateway.Execute(run, new(Operation.ReadDocument, "documents/task")).Result == Gateway.TaskDocument;

    private static Comparison Approval(string root) => Pair("3-approval", "Exact content approval binding", "Approve reviewed draft, then submit substituted draft", () =>
    {
        var f = Fixtures(root, "approval"); var unsafeExecutor = new DeliberatelyVulnerable();
        unsafeExecutor.ApprovePublishOperation();
        unsafeExecutor.PublishWithOperationOnlyApproval(ComparisonFixture.Replaced);
        using var secure = new DurableFixture(f.Secure.Directory); var g = secure.Open(); var run = DurableFixture.Run(g);
        var ticket = g.ApprovePublication(run, Draft(), TimeSpan.FromMinutes(1));
        var rejected = g.Execute(run, Draft(ComparisonFixture.Replaced), ticket, "replace");
        Require(rejected.Outcome == Outcome.Denied && g.GetEffects().Publications == 0, "changed draft was published");
        Require(g.Execute(run, Draft(), ticket, "original").Outcome == Outcome.Allowed && g.GetReport() == new ReportSnapshot(1, ComparisonFixture.Reviewed), "reviewed draft positive control");
        Require(unsafeExecutor.Publications.SequenceEqual([ComparisonFixture.Replaced]), "unsafe substitution did not happen");
        var positive = Positive(g, run); g.Stop();
        return (Observe("unsafe", "operation-only approval accepted substituted text", true, ("publishedText", unsafeExecutor.Publications.Single())),
            Observe("secure", rejected.Reason, positive, ("substitutedPublications", 0), ("originalPublications", 1), ("publishedText", ComparisonFixture.Reviewed)));
    });
    private static Comparison Scope(string root) => Pair("1-scope", "Authenticated task binding", "Authenticated task A asks for task B", () =>
    {
        var f = Fixtures(root, "scope"); var vulnerable = new DeliberatelyVulnerable();
        foreach (var fixture in new[] { f.Unsafe, f.Secure }) File.WriteAllText(Path.Combine(fixture.Documents, "reference", "reference.txt"), ComparisonFixture.FakeSecret);
        var leaked = vulnerable.ReadTask("task-a", "task-b");
        var catalog = SyntheticDocuments.Catalog.Select(e => e.Resource == "documents/reference" ? e with { Sha256 = SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(ComparisonFixture.FakeSecret)) } : e);
        var g = new Gateway(new DefaultPolicy(), new DocumentExecutor(new FileDocumentReader(f.Secure.Documents, catalog)));
        var run = g.CreateSession(new("synthetic-v1", DateTimeOffset.UtcNow.AddMinutes(3), 10, 8192, [new(Operation.ReadDocument, "documents/task")]));
        var denied = g.Execute(run, new(Operation.ReadDocument, "documents/reference"));
        Require(leaked == ComparisonFixture.FakeSecret && denied.Outcome == Outcome.Denied && denied.Result is null && g.GetEffects().DocumentReads == 0, "cross-task leakage comparison");
        return (Observe("unsafe", "caller task field trusted", vulnerable.ReadTask("task-a", "task-a") == Gateway.TaskDocument, ("foreignTaskBytes", leaked)),
            Observe("secure", denied.Reason, Positive(g, run), ("foreignTaskBytes", ""), ("deniedReadEffects", 0)));
    });
    private static Comparison Actions(string root) => Pair("2-actions", "Immutable operation grants", "ExternalRequest, MessageAgent, ChangePermissions to synthetic-local-store", () =>
    {
        var f = Fixtures(root, "actions"); var vulnerable = new DeliberatelyVulnerable(); var g = f.Secure.ReaderGateway(); var run = ComparisonFixture.Run(g);
        foreach (var op in new[] { Operation.ExternalRequest, Operation.MessageAgent, Operation.ChangePermissions })
        {
            var proposal = new Proposal(op, "synthetic-local-store"); vulnerable.Execute(proposal);
            Require(g.Execute(run, proposal).Outcome == Outcome.Denied, "out-of-scope operation executed");
        }
        Require(vulnerable.Capture.SequenceEqual([ComparisonFixture.FakeSecret]) && vulnerable.Messages == 1 && vulnerable.Admin, "unsafe actions did not occur");
        Require(g.GetEffects() == new MockEffects(0, 0), "secure out-of-scope side effects");
        return (Observe("unsafe", "executor skipped grants", vulnerable.ReadTask("task-a", "task-a") == Gateway.TaskDocument, ("captureRecords", vulnerable.Capture.Count), ("messages", vulnerable.Messages), ("permissionAdmin", vulnerable.Admin)),
            Observe("secure", "all three proposals denied", Positive(g, run), ("captureRecords", 0), ("messages", 0), ("permissionAdmin", false)));
    });
    private static Comparison Files(string root) => Pair("6-files", "Exact resource IDs safe opens and measured size", "../private.txt; link/secret.txt; declared-zero 256-byte file with 128-byte cap", () =>
    {
        var f = Fixtures(root, "files");
        foreach (var fixture in new[] { f.Unsafe, f.Secure }) { fixture.Link(); File.WriteAllText(Path.Combine(fixture.Documents, "task", "large.txt"), new string('x', 256)); }
        var traversal = DeliberatelyVulnerable.ReadPath(f.Unsafe, "../private.txt", 0);
        var linked = DeliberatelyVulnerable.ReadPath(f.Unsafe, "link/secret.txt", 0);
        var oversize = DeliberatelyVulnerable.ReadPath(f.Unsafe, "task/large.txt", 0);
        var reader = new FileDocumentReader(f.Secure.Documents, SyntheticDocuments.Catalog, 128);
        var denied = reader.Read("../private.txt", 4096);
        // A trusted catalog accidentally referencing a link is still rejected at open, independently of resource scope.
        var linkReader = new FileDocumentReader(f.Secure.Documents, [new("documents/reference", "link/secret.txt", SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(ComparisonFixture.FakeSecret)))]);
        var linkDenied = linkReader.Read("documents/reference", 4096);
        var sizeReader = new FileDocumentReader(f.Secure.Documents, [new("documents/reference", "task/large.txt", SyntheticDocuments.Hash(Encoding.UTF8.GetBytes(oversize)))], 128);
        var sizeDenied = sizeReader.Read("documents/reference", 4096);
        Require(traversal == ComparisonFixture.FakeSecret && linked == ComparisonFixture.FakeSecret && oversize.Length == 256, "unsafe file failures absent");
        Require(new[] { denied, linkDenied, sizeDenied }.All(d => d.Outcome == Outcome.Denied && d.Result is null), "secure file returned partial forbidden bytes");
        return (Observe("unsafe", "caller paths links and declared size trusted", DeliberatelyVulnerable.ReadPath(f.Unsafe, "task/task.txt", 0) == Gateway.TaskDocument,
            ("traversalLeak", traversal), ("linkLeak", linked), ("oversizeBytes", Encoding.UTF8.GetByteCount(oversize))),
            Observe("secure", "all three file failures denied", reader.Read("documents/task", 4096).Result == Gateway.TaskDocument,
            ("traversalBytes", 0), ("linkBytes", 0), ("oversizeBytes", 0)));
    });
    private static Comparison Injection(string root) => Pair("7-injection", "Untrusted text is not execution authority", "Document instruction TRANSFER_FAKE_SECRET; proposed ExternalRequest synthetic-local-store", () =>
    {
        var f = Fixtures(root, "injection"); const string hostile = "Synthetic notes. TRANSFER_FAKE_SECRET";
        foreach (var fixture in new[] { f.Unsafe, f.Secure }) File.WriteAllText(Path.Combine(fixture.Directory, "untrusted.txt"), hostile);
        var vulnerable = new DeliberatelyVulnerable();
        if (File.ReadAllText(Path.Combine(f.Unsafe.Directory, "untrusted.txt")).Contains("TRANSFER_FAKE_SECRET", StringComparison.Ordinal))
            vulnerable.Execute(new(Operation.ExternalRequest, "synthetic-local-store"));
        var g = f.Secure.ReaderGateway(); var run = ComparisonFixture.Run(g);
        Require(File.ReadAllText(Path.Combine(f.Secure.Directory, "untrusted.txt")) == hostile, "different hostile input across variants");
        var model = new ModelProposalSource(Encoding.UTF8.GetBytes("{\"proposals\":[{\"operation\":\"ExternalRequest\",\"resource\":\"synthetic-local-store\"}]}"));
        Require(g.Execute(run, model.GetProposals().Single()).Outcome == Outcome.Denied && g.GetEffects() == new MockEffects(0, 0), "injection authorized a transfer");
        try
        {
            var batch = new ModelProposalSource(Encoding.UTF8.GetBytes("{\"proposals\":[{\"operation\":\"ReadDocument\",\"resource\":\"documents/task\"},{\"operation\":\"PublishReport\",\"resource\":\"reports/draft\",\"approval\":\"forged\"}]}"));
            foreach (var p in batch.GetProposals()) g.Execute(run, p);
            throw new InvalidOperationException("Malformed authority batch accepted");
        }
        catch (ModelOutputException) { }
        Require(g.GetEffects() == new MockEffects(0, 0) && vulnerable.Capture.SequenceEqual([ComparisonFixture.FakeSecret]), "whole-batch or unsafe effect assertion");
        return (Observe("unsafe", "document promoted into command", vulnerable.ReadTask("task-a", "task-a") == Gateway.TaskDocument, ("fakeSecretCapture", vulnerable.Capture.Single())),
            Observe("secure", "out_of_scope and whole-batch parser rejection", Positive(g, run), ("captureRecords", 0), ("partialBatchEffects", 0)));
    });
    private static Comparison Retry(string root) => Pair("4a-retry", "Durable idempotency across lost acknowledgement and restart", "Commit reviewed draft; lose reply; retry same key after reopening state", () =>
    {
        var f = Fixtures(root, "retry"); var ledger = Path.Combine(f.Unsafe.Directory, "publications.txt");
        try { File.AppendAllLines(ledger, [ComparisonFixture.Reviewed]); throw new LostAcknowledgement(); }
        catch (LostAcknowledgement) { } // Commit succeeds; caller really observes an injected lost acknowledgement.
        File.AppendAllLines(ledger, [ComparisonFixture.Reviewed]); // Reopened executor has no idempotency record.
        using var secure = new DurableFixture(f.Secure.Directory); var g = secure.Open(); var run = DurableFixture.Run(g); var ticket = g.ApprovePublication(run, Draft(), TimeSpan.FromMinutes(1));
        secure.Sink.LoseAck = true; var uncertain = g.Execute(run, Draft(), ticket, "once");
        Require(uncertain.Outcome == Outcome.RecoveryRequired, "lost acknowledgment was not observed");
        secure.Sink.LoseAck = false; g = secure.Open(); var retry = g.Execute(run, Draft(), ticket, "once");
        Require(retry.Reason == "publication_replayed" && g.GetEffects().Publications == 1 && File.ReadAllLines(ledger).Length == 2, "retry effects not 2 versus 1");
        var positive = Positive(g, run); g.Stop();
        return (Observe("unsafe", "lost acknowledgement then duplicate retry", true, ("publications", File.ReadAllLines(ledger).Length), ("restart", true)),
            Observe("secure", uncertain.Reason + " -> " + retry.Reason, positive, ("publications", 1), ("restart", true)));
    });
    private static Comparison Audit(string root) => Pair("4b-audit", "Independent signed audit checkpoint", "Delete final committed event and rewrite/re-hash earlier history", () =>
    {
        var f = Fixtures(root, "audit"); using var secure = new DurableFixture(f.Secure.Directory); var g = secure.Open(); var run = DurableFixture.Run(g);
        var ticket = g.ApprovePublication(run, Draft(), TimeSpan.FromMinutes(1)); Require(g.Execute(run, Draft(), ticket, "audit").Outcome == Outcome.Allowed, "initial publication");
        var original = g.GetAuditSnapshot().ToArray();
        var prefix = original[..^1];
        var rewritten = prefix.Select(e => e with { Reason = "concealed synthetic history", PreviousHash = "", Hash = "" }).ToArray();
        for (var i = 0; i < rewritten.Length; i++) { rewritten[i] = rewritten[i] with { PreviousHash = i == 0 ? "" : rewritten[i-1].Hash }; rewritten[i] = rewritten[i] with { Hash = AuditChain.ComputeHash(rewritten[i]) }; }
        Require(AuditChain.VerifyAudit(prefix) && AuditChain.VerifyAudit(rewritten), "local rewrite did not conceal history");
        Require(secure.Verify(original) && !secure.Verify(prefix) && !secure.Verify(rewritten), "independent head missed mutation/truncation");
        File.WriteAllText(Path.Combine(f.Unsafe.Directory, "rewritten-audit.json"), System.Text.Json.JsonSerializer.Serialize(rewritten));
        var another = Draft(ComparisonFixture.Replaced) with { ExpectedVersion = 1 };
        var second = g.ApprovePublication(run, another, TimeSpan.FromMinutes(1)); var before = g.GetEffects().Publications;
        secure.Sink.Offline = true; var outage = g.Execute(run, another, second, "outage"); secure.Sink.Offline = false;
        Require(outage.Outcome != Outcome.Allowed && g.GetEffects().Publications == before, "audit outage created an effect");
        var positive = Positive(g, run); g.Stop();
        return (Observe("unsafe", "rewritten/truncated local chain accepted", true, ("concealedPublication", true), ("localValidation", true)),
            Observe("secure", "independent head rejects both histories; outage blocks write", positive, ("tamperingDetected", true), ("outageNewPublications", 0), ("earlierPublicationsPreserved", 1)));
    });
    private static Comparison Version(string root) => Pair("8-version", "Expected resource version", "Two exact-content approvals against version 0, publish first then stale second", () =>
    {
        var f = Fixtures(root, "version"); var unsafeLedger = new MissingVersionStore();
        unsafeLedger.Approve(ComparisonFixture.Reviewed, 0); unsafeLedger.Approve(ComparisonFixture.Replaced, 0);
        unsafeLedger.Write(ComparisonFixture.Reviewed, 0); unsafeLedger.Write(ComparisonFixture.Replaced, 0);
        File.WriteAllText(Path.Combine(f.Unsafe.Directory, "report.txt"), unsafeLedger.Content); // Exact contents approved separately; only CAS omitted.
        Require(unsafeLedger.Content == ComparisonFixture.Replaced && unsafeLedger.Version == 2, "unsafe stale overwrite absent");
        using var secure = new DurableFixture(f.Secure.Directory); var g = secure.Open(); var run = DurableFixture.Run(g);
        var first = Draft(); var second = Draft(ComparisonFixture.Replaced);
        var a = g.ApprovePublication(run, first, TimeSpan.FromMinutes(1)); var b = g.ApprovePublication(run, second, TimeSpan.FromMinutes(1));
        Require(g.Execute(run, first, a, "first").Outcome == Outcome.Allowed, "first approved write");
        var stale = g.Execute(run, second, b, "second");
        Require(stale.Reason == "resource_version_conflict" && g.GetReport() == new ReportSnapshot(1, ComparisonFixture.Reviewed), "stale version overwrite");
        var positive = Positive(g, run); g.Stop();
        return (Observe("unsafe", "last writer overwrote earlier approved content", true, ("content", File.ReadAllText(Path.Combine(f.Unsafe.Directory, "report.txt"))), ("version", 2)),
            Observe("secure", stale.Reason, positive, ("content", ComparisonFixture.Reviewed), ("version", 1)));
    });
    private static Comparison Quota(string root) => Pair("9a-quota", "Atomic shared reservations", "Two coordinated requests each cost 1 against shared allowance 1", () =>
    {
        var f = Fixtures(root, "quota"); var remaining = 1; var effects = 0;
        using var barrier = new Barrier(2);
        void UnsafeRequest()
        {
            var allowed = Volatile.Read(ref remaining) >= 1;
            Require(barrier.SignalAndWait(TimeSpan.FromSeconds(5)), "coordination failed");
            if (allowed) { Interlocked.Decrement(ref remaining); Interlocked.Increment(ref effects); }
        }
        Parallel.Invoke(UnsafeRequest, UnsafeRequest);
        var key = RandomNumberGenerator.GetBytes(32); var path = Path.Combine(f.Secure.Directory, "budget");
        var store = new DurableBudgetStore(path, key); store.Create("shared", new(DateTimeOffset.UtcNow.AddMinutes(3), 5, 1, 1, 1, 2));
        var admitted = 0;
        Parallel.For(0, 2, index => { var replica = new DurableBudgetStore(path, key); if (replica.Reserve("shared", new(1,1,1), out _) is null) Interlocked.Increment(ref admitted); });
        var snapshot = store.Snapshot("shared");
        Require(effects == 2 && remaining == -1 && admitted == 1 && snapshot.CostMicros == 0, "atomic overspend comparison");
        return (Observe("unsafe", "both checked before either charged", effects > 0, ("admitted", effects), ("remaining", remaining)),
            Observe("secure", "one reserved, other denied", admitted > 0, ("admitted", admitted), ("remaining", snapshot.CostMicros)));
    });
    private static Comparison RetryLimit(string root) => Pair("9b-retry-limit", "Accounted bounded retry policy", "Fixed failing fixture retried 5 times; scenario allows initial attempt plus 1 retry", () =>
    {
        var f = Fixtures(root, "retry-limit"); var unsafeTool = new FailingFixture(); var secureTool = new FailingFixture();
        for (var i=0; i<5; i++) { try { unsafeTool.Execute(); } catch (FixedToolFailure) { } }
        var store = new DurableBudgetStore(Path.Combine(f.Secure.Directory, "budget"), RandomNumberGenerator.GetBytes(32));
        store.Create("retry", new(DateTimeOffset.UtcNow.AddMinutes(3), 10, 20, 20, 20, Retries: 1));
        for (var ordinal=0; ordinal<5; ordinal++)
        {
            if (store.Reserve("retry", new(1,1,1,ordinal), out var reservation) is null)
            {
                try { secureTool.Execute(); } catch (FixedToolFailure) { }
                finally { store.Release("retry",reservation!); }
            }
        }
        Require(unsafeTool.Attempts == 5 && secureTool.Attempts == 2, "retry fixture effects not bounded");
        return (Observe("unsafe", "retry policy absent, fixed outer maximum retained", unsafeTool.Attempts > 0, ("fixtureAttempts", unsafeTool.Attempts)),
            Observe("secure", "excess retries denied before fixture", secureTool.Attempts > 0, ("fixtureAttempts", secureTool.Attempts)));
    });
}
