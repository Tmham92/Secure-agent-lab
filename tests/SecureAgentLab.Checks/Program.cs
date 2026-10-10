using SecureAgentLab.Checks.Cases;

using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.StartForAssembly("core-checks");
var checks = new (string Name, Action Run)[]
{
    ("Unknown identity cannot execute", () => UnknownIdentityCannotExecuteCase.Run()),
    ("Expiry at exact deadline and revocation", () => ExpiryAtExactDeadlineAndRevocationCase.Run()),
    ("Exact read measures UTF-8 content", () => ExactReadMeasuresUTFContentCase.Run()),
    ("Scope rejects traversal and all unsupported operations", () => ScopeRejectsTraversalAndAllUnsupportedOperationsCase.Run()),
    ("Malformed proposals fail closed and consume calls", () => MalformedProposalsFailClosedAndConsumeCallsCase.Run()),
    ("Publication requires host approval; forged and replayed tickets denied", () => PublicationRequiresHostApprovalForgedAndReplayedTicketsDeniedCase.Run()),
    ("Approval expires at deadline", () => ApprovalExpiresAtDeadlineCase.Run()),
    ("Approval cannot cross sessions or altered proposals", () => ApprovalCannotCrossSessionsOrAlteredProposalsCase.Run()),
    ("Actual byte limits defeat zero estimates and prevent effects", () => ActualByteLimitsDefeatZeroEstimatesAndPreventEffectsCase.Run()),
    ("Cumulative response quota and publication accounting", () => CumulativeResponseQuotaAndPublicationAccountingCase.Run()),
    ("Denied attempts exhaust call budget", () => DeniedAttemptsExhaustCallBudgetCase.Run()),
    ("Concurrent requests cannot overspend call quota", () => ConcurrentRequestsCannotOverspendCallQuotaCase.Run()),
    ("Concurrent requests cannot overspend byte quota", () => ConcurrentRequestsCannotOverspendByteQuotaCase.Run()),
    ("Concurrent approval redemption executes once", () => ConcurrentApprovalRedemptionExecutesOnceCase.Run()),
    ("Stop blocks execution and further host issuance", () => StopBlocksExecutionAndFurtherHostIssuanceCase.Run()),
    ("Invalid host configuration rejected; zero quotas valid", () => InvalidHostConfigurationRejectedZeroQuotasValidCase.Run()),
    ("Audit mutation, reorder, removal and snapshot isolation", () => AuditMutationReorderRemovalAndSnapshotIsolationCase.Run())
};
int failures = 0;
foreach ((string? name, Action? run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception e)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {e}");
    }
}

Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;
