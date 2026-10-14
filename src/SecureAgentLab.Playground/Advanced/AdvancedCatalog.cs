namespace SecureAgentLab.Playground.Advanced;

public static class AdvancedCatalog
{
    private static AdvancedScenario Pair(string id, string title, string attack, string defense, string expected) =>
        new(id, title, "Baseline vs hardened", attack, defense, expected,
            $"dotnet run --project src/SecureAgentLab.Comparisons -c Release -- --playground {id}",
            "src/SecureAgentLab.Comparisons", ["--playground", id], "playground/advanced/" + id);
    public static IReadOnlyList<AdvancedScenario> All
    {
        get;
    } = Array.AsReadOnly<AdvancedScenario>(
    [
        Pair("approval", "Exact-content approval", "Substitute a different draft after approval.", "Bind approval to exact reviewed content.", "Baseline publishes the substituted draft; hardened publishes only the reviewed draft."),
        Pair("scope", "Cross-task resource access", "Read another task's document with a valid identity.", "Scope authorization to the current task and resource.", "Cross-task content leaks only in baseline; legitimate reads still succeed."),
        Pair("actions", "Excessive tool authority", "Request tools outside the task's authorized operations.", "Default-deny operation allowlists.", "Unauthorized synthetic effects occur only in baseline."),
        Pair("files", "Filesystem traversal, links and size", "Use traversal, a junction/symlink, and a false size estimate.", "Exact resource IDs, pinned no-link opens and measured byte limits.", "Baseline returns forbidden bytes; hardened rejects all three attempts."),
        Pair("injection", "Document-to-command injection", "Promote hostile document text into an external transfer.", "Treat text as data; authorize each operation and reject malformed proposal batches.", "The fake secret is captured only in baseline; malformed batches have no partial effects."),
        Pair("retry", "Lost acknowledgement and duplicate effects", "Retry a committed publication after losing its reply and reopening state.", "Durable idempotency and reconciliation.", "Baseline publishes twice; hardened records one publication across restart."),
        Pair("audit", "Audit rewrite, truncation and outage", "Rewrite/re-hash or truncate history; then interrupt the audit sink.", "Independent signed checkpoint and fail-closed writes.", "Local chain alone accepts concealed history; hardened detects tampering and prevents new writes during outage."),
        Pair("version", "Stale approved overwrite", "Use two approvals against the same old resource version.", "Compare expected version atomically before committing.", "Baseline overwrites the first draft; hardened rejects the stale second write."),
        Pair("quota", "Concurrent budget overspend", "Two requests check the same remaining allowance before either charges it.", "Atomic durable reservations shared across instances.", "Baseline admits two against allowance one; hardened admits one."),
        Pair("retry-limit", "Retry amplification", "Retry a failing tool five times.", "Accounted bounded retries.", "Baseline attempts five executions; hardened allows the initial attempt and one retry."),
        new("durable", "Durable approval and crash recovery", "Security checks", "Restart, race approval redemption, inject commit crashes, or tamper with persisted state.", "Durable exact approval, idempotent recovery and independently signed audit.", "All 20 existing durable checks pass. This is a hardened check suite, not a vulnerable comparison.", "dotnet run --project tests/SecureAgentLab.DurableChecks -c Release", "tests/SecureAgentLab.DurableChecks", [], "durable-checks"),
        new("collaboration", "Multi-agent identity and channel security", "Security checks", "Cross run boundaries, forge supervisory messages, or bypass allowed communication channels.", "Broker-enforced identities, task/topic grants, measured quotas and independent evaluation.", "All 18 existing collaboration checks pass. This is a hardened check suite, not a vulnerable comparison.", "dotnet run --project tests/SecureAgentLab.CollaborationChecks -c Release", "tests/SecureAgentLab.CollaborationChecks", [], "collaboration-checks"),
        new("isolation", "Container filesystem and network isolation", "Docker exercise", "Read a mounted fake secret and send it over a direct local route.", "Remove the sensitive mount and restrict network paths inside an outer disposable boundary.", "Unsafe container captures the fake secret; secure container blocks direct access while permitted relay reads succeed.", "./scripts/Run-ContainerComparisons.ps1 -Scenario isolation", null, [], "comparisons/container"),
        new("containment", "Container stop and late-effect containment", "Docker exercise", "A child process writes after a flag-only stop was acknowledged.", "Revoke authority and terminate the owned child process before acknowledging containment.", "Unsafe child writes late; secure child cannot. Earlier legitimate effects remain preserved.", "./scripts/Run-ContainerComparisons.ps1 -Scenario containment", null, [], "comparisons/container")
    ]);
    public static AdvancedScenario Find(string id) => All.SingleOrDefault(s => s.Id == id) ?? throw new ArgumentException("Unknown advanced scenario.", nameof(id));
}
