using SecureAgentLab.CollaborationChecks.Cases;
using SecureAgentLab.CollaborationChecks.Fixtures;

if (args.Contains("--demo"))
{
    await DemoRunner.Run();
    return;
}

using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.StartForAssembly("collaboration-checks");
var tests = new List<(string, Func<Task>)>();
void Add(string name, Action action) => tests.Add((name, () =>
{
    action();
    return Task.CompletedTask;
}
));
void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

Add("Authorized two-agent methods and correct answer pass independent verification", () => AuthorizedTwoAgentMethodsAndCorrectAnswerPassIndependentVerificationCase.Run(Assert));
Add("Grant copies prevent caller mutation and binding forgery", () => GrantCopiesPreventCallerMutationAndBindingForgeryCase.Run(Assert));
Add("Cross-run delivery and consumption are rejected with no effects", () => CrossRunDeliveryAndConsumptionAreRejectedWithNoEffectsCase.Run(Assert));
Add("Sender recipient and topic grants default deny", () => SenderRecipientAndTopicGrantsDefaultDenyCase.Run(Assert));
Add("Supervisory-looking text remains worker data and cannot score a method", () => SupervisoryLookingTextRemainsWorkerDataAndCannotScoreAMethodCase.Run(Assert));
Add("Denied attempts exhaust call quota", () => DeniedAttemptsExhaustCallQuotaCase.Run(Assert));
Add("Concurrent sends atomically enforce quota and one-consumer mailbox", () => ConcurrentSendsAtomicallyEnforceQuotaAndOneConsumerMailboxCase.Run(Assert));
Add("Measured UTF8 and receive byte budgets reject before mailbox effects", () => MeasuredUTF8AndReceiveByteBudgetsRejectBeforeMailboxEffectsCase.Run(Assert));
Add("Oversized message and bounded queue fail closed", () => OversizedMessageAndBoundedQueueFailClosedCase.Run(Assert));
Add("Expiry revocation and stop prevent later effects", () => ExpiryRevocationAndStopPreventLaterEffectsCase.Run(Assert));
Add("Shared cache artifacts and writable logs expose no alternate channel", () => SharedCacheArtifactsAndWritableLogsExposeNoAlternateChannelCase.Run(Assert));
Add("Correct answer alone fails while wrong answer with correct method also fails", () => CorrectAnswerAloneFailsWhileWrongAnswerWithCorrectMethodAlsoFailsCase.Run(Assert));
Add("A completed exchange with substituted facts or supervisory instructions fails scoring", () => ACompletedExchangeWithSubstitutedFactsOrSupervisoryInstructionsFailsScoringCase.Run(Assert));
Add("Signed evidence rejects mutation truncation and unrelated signing keys", () => SignedEvidenceRejectsMutationTruncationAndUnrelatedSigningKeysCase.Run(Assert));
Add("Challenge run binding and freshness reject signed stale evidence", () => ChallengeRunBindingAndFreshnessRejectSignedStaleEvidenceCase.Run(Assert));
Add("Audit capacity exhaustion stops effects without dropping earlier evidence", () => AuditCapacityExhaustionStopsEffectsWithoutDroppingEarlierEvidenceCase.Run(Assert));
tests.Add(("HTTP identity schema operator and evaluator boundaries reject tampering", () => HTTPIdentitySchemaOperatorAndEvaluatorBoundariesRejectTamperingCase.RunAsync(Assert)));
tests.Add(("Collaboration APIs require HTTPS unless loopback HTTP is explicitly enabled", () => CollaborationAPIsRequireHTTPSUnlessLoopbackHTTPIsExplicitlyEnabledCase.RunAsync(Assert)));
int failures = 0;
foreach ((string? name, Func<Task>? test) in tests)
{
    try
    {
        await test();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + ex);
    }
}

Console.WriteLine($"Phase 8: {tests.Count - failures}/{tests.Count} checks passed.");
Environment.ExitCode = failures == 0 ? 0 : 1;
