using System.Security.Cryptography;
using SecureAgentLab.Core.Contracts;
using SecureAgentLab.Durable.Models;
using SecureAgentLab.DurableChecks.Cases;
using SecureAgentLab.DurableChecks.Fixtures;

if (args.Contains("--hang-fixture"))
{
    Console.WriteLine("READY");
    await Task.Delay(Timeout.Infinite);
    return 0;
}

if (args.Contains("--phase7") || args.Contains("--drill"))
{
    return await Phase7Checks.Run(FindRoot());
}

using var rsa = RSA.Create(2048);
string privateKey = rsa.ExportPkcs8PrivateKeyPem();
using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.StartForAssembly("durable-checks");
string fixturesRoot = artifactRun.DirectoryPath;
var tests = new List<(string Name, Func<Task> Check)>();
var read = new Proposal(Operation.ReadDocument, "documents/task");
var draft = new Proposal(Operation.PublishReport, "reports/draft", 0, "Synthetic approved content.");
Fixture NewFixture() => new(fixturesRoot, privateKey);
void Add(string name, Action check) => tests.Add((name, () =>
{
    check();
    return Task.CompletedTask;
}
));
Add("Sessions, budgets and approvals survive restart", () => SessionsBudgetsAndApprovalsSurviveRestartCase.Run(draft, read, NewFixture));
Add("Exact content, destination, estimate and session binding", () => ExactContentDestinationEstimateAndSessionBindingCase.Run(draft, NewFixture));
Add("Parallel redemption across gateway instances executes once", () => ParallelRedemptionAcrossGatewayInstancesExecutesOnceCase.Run(draft, NewFixture));
Add("Parallel idempotent retries preserve one effect and charge responses", () => ParallelIdempotentRetriesPreserveOneEffectAndChargeResponsesCase.Run(draft, NewFixture));
Add("Policy version change invalidates outstanding grants and approvals", () => PolicyVersionChangeInvalidatesOutstandingGrantsAndApprovalsCase.Run(draft, NewFixture));
Add("Approval expiry and revoked runs remain blocked after restart", () => ApprovalExpiryAndRevokedRunsRemainBlockedAfterRestartCase.Run(draft, NewFixture));
foreach (DurabilityPoint point in Enum.GetValues<DurabilityPoint>())
{
    Add($"Publication recovers exactly once after {point}", () => PublicationRecoversExactlyOnceAfterPointCase.Run(draft, point, NewFixture));
}

Add("Audit collector recovers a complete signed suffix after head-write crash", () => AuditCollectorRecoversACompleteSignedSuffixAfterHeadWriteCrashCase.Run(privateKey, read, NewFixture));
Add("Audit outage rejects writes before effects or approval consumption", () => AuditOutageRejectsWritesBeforeEffectsOrApprovalConsumptionCase.Run(draft, NewFixture));
Add("Lost acknowledgement reports recovery required and reconciles once", () => LostAcknowledgementReportsRecoveryRequiredAndReconcilesOnceCase.Run(draft, NewFixture));
Add("Stop and revoke markers contain work during sink outage", () => StopAndRevokeMarkersContainWorkDuringSinkOutageCase.Run(read, NewFixture));
Add("Audit contains control events and redacts content, tickets and arbitrary identifiers", () => AuditContainsControlEventsAndRedactsContentTicketsAndArbitraryIdentifiersCase.Run(draft, NewFixture));
Add("Tail truncation is detected using independent signed head", () => TailTruncationIsDetectedUsingIndependentSignedHeadCase.Run(read, NewFixture));
Add("Rehashed audit history cannot forge checkpoint signatures", () => RehashedAuditHistoryCannotForgeCheckpointSignaturesCase.Run(read, NewFixture));
Add("State alteration, state rollback and missing state fail closed", () => StateAlterationStateRollbackAndMissingStateFailClosedCase.Run(read, NewFixture));
Add("Unsigned prepared-state mutation cannot authorize a publication", () => UnsignedPreparedStateMutationCannotAuthorizeAPublicationCase.Run(draft, NewFixture));
Add("Clock regression and byte-limit failures never publish", () => ClockRegressionAndByteLimitFailuresNeverPublishCase.Run(draft, NewFixture));
// Real HTTP collector + gateway, followed by reconstructing the gateway against the same durable state.
tests.Add(("HTTP collector and gateway preserve credentials, approvals and effects across restart", () => HTTPCollectorAndGatewayPreserveCredentialsApprovalsAndEffectsAcrossRestartCase.RunAsync(draft, privateKey, rsa, NewFixture)));
int failures = 0;
foreach ((string? name, Func<Task>? check) in tests)
{
    try
    {
        await check();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception e)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {e}");
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} durable checks passed.");
return failures == 0 ? 0 : 1;

static string FindRoot()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "SecureAgentLab.slnx")))
    {
        d = d.Parent;
    }

    return d?.FullName ?? throw new InvalidOperationException("Repository missing");
}
