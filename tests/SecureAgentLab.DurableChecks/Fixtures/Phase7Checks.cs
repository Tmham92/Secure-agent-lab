using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Audit;
using SecureAgentLab.Durable.Budgets;
using SecureAgentLab.Durable.Execution;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Cases.Runtime;
namespace SecureAgentLab.DurableChecks.Fixtures;

internal static class Phase7Checks
{
    public static async Task<int> Run(string root)
    {
        using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.Start(root, "phase7");
        string evidence = artifactRun.DirectoryPath;
        Directory.CreateDirectory(evidence);
        using var rsa = RSA.Create(2048);
        string privateKey = rsa.ExportPkcs8PrivateKeyPem();
        var tests = new List<(string, Func<Task>)>();
        Fixture Make() => new(evidence, privateKey);
        void Add(string name, Action check) => tests.Add((name, () =>
        {
            check();
            return Task.CompletedTask;
        }
        ));
        var draft = new Proposal(Operation.PublishReport, "reports/draft", Content: "Reviewed synthetic report.", ExpectedVersion: 0);
        Add("Version and exact content are approved; stale parallel approval cannot overwrite", () => VersionAndExactContentAreApprovedStaleParallelApprovalCannotOverwriteCase.Run(draft, Make));
        foreach (DurabilityPoint point in Enum.GetValues<DurabilityPoint>())
        {
            Add($"Versioned write recovers exactly once after {point}", () => VersionedWriteRecoversExactlyOnceAfterPointCase.Run(draft, point, Make));
        }

        Add("Audit outage fails closed before report mutation", () => AuditOutageFailsClosedBeforeReportMutationCase.Run(draft, Make));
        Add("Shared durable budget rejects reset, retry, fanout and survives restart", () => SharedDurableBudgetRejectsResetRetryFanoutAndSurvivesRestartCase.Run(Make));
        Add("Parallel replicas cannot overspend monetary or token ceilings", () => ParallelReplicasCannotOverspendMonetaryOrTokenCeilingsCase.Run(Make));
        Add("Budget tampering and missing state fail closed", () => BudgetTamperingAndMissingStateFailClosedCase.Run(Make));
        tests.Add(("Hanging cooperative tool times out, revokes run and creates no effect", () => HangingCooperativeToolTimesOutRevokesRunAndCreatesNoEffectCase.RunAsync(draft, Make)));
        tests.Add(("Replica revocation and global stop cancel active work", () => ReplicaRevocationAndGlobalStopCancelActiveWorkCase.RunAsync(draft, Make)));
        tests.Add(("Noncooperative late tool remains quarantined and its later proposal is denied", () => NoncooperativeLateToolRemainsQuarantinedAndItsLaterProposalIsDeniedCase.RunAsync(Make)));
        tests.Add(("Owned hanging worker process is terminated on cancellation", () => OwnedHangingWorkerProcessIsTerminatedOnCancellationCase.RunAsync(Make)));
        tests.Add(("Authenticated HTTP versioned write uses trusted runtime admission and blocks worker report access", () => AuthenticatedHTTPVersionedWriteUsesTrustedRuntimeAdmissionAndBlocksWorkerReportAccessCase.RunAsync(draft, Make)));
        int failures = 0;
        var results = new List<object>();
        foreach ((string? name, Func<Task>? check) in tests)
        {
            var elapsed = Stopwatch.StartNew();
            bool passed = false;
            try
            {
                await check();
                passed = true;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception e)
            {
                failures++;
                Console.Error.WriteLine("FAIL " + name + ": " + e);
            }

            results.Add(new
            {
                Scenario = name,
                Passed = passed,
                ElapsedMilliseconds = elapsed.ElapsedMilliseconds
            });
        }

        File.WriteAllText(Path.Combine(evidence, "evidence.json"), JsonSerializer.Serialize(new
        {
            Phase = 7,
            Checks = tests.Count,
            Passed = tests.Count - failures,
            Utc = DateTimeOffset.UtcNow,
            Results = results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{tests.Count - failures}/{tests.Count} phase 7 checks passed. Evidence: {evidence}");
        return failures == 0 ? 0 : 1;
    }

    internal static DurableGateway Open(Fixture f, Action<DurabilityPoint>? crash = null) => new(f.State, f.Sink, ((AuditCollectorStore)f.Sink.Inner).PublicKey, f.Stream, f.IntegrityKey, clock: f.Clock, crash: crash, enableReportWrites: true);
    internal static DurableBudgetStore Budget(Fixture f, string run)
    {
        var b = new DurableBudgetStore(Path.Combine(f.State, "budget"), f.IntegrityKey, f.Clock);
        b.Create(run, new(f.Clock.GetUtcNow().AddMinutes(1), 30, 100, 100, 100));
        return b;
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static void Equal<T>(T expected, T actual) => Assert(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}, got {actual}");
    internal static void Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
