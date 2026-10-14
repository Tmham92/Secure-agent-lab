using System.Text;
using System.Text.Json;
using SecureAgentLab.Core.Proposals;
using SecureAgentLab.ModelChecks.Cases;
using SecureAgentLab.ModelChecks.Fixtures;
using var artifactRun = global::SecureAgentLab.Core.Diagnostics.ArtifactRun.StartForAssembly("model-checks");
var checks = new List<(string Name, Func<Task> Run)>();
byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
const string Valid = """{"proposals":[{"operation":"ReadDocument","resource":"documents/task"}]}""";
void Add(string name, Action action) => checks.Add((name, () =>
{
    action();
    return Task.CompletedTask;
}
));
Add("Valid batch yields immutable typed proposals", () => ValidBatchYieldsImmutableTypedProposalsCase.Run(Valid, Bytes));
Add("Unknown authority fields, duplicates and alternate enum encodings rejected", () => UnknownAuthorityFieldsDuplicatesAndAlternateEnumEncodingsRejectedCase.Run(Valid, Reject));
Add("Malformed, truncated, fenced, wrong-type and oversized output rejected", () => MalformedTruncatedFencedWrongTypeAndOversizedOutputRejectedCase.Run(Valid, Reject));
Add("Batch bounds and late malformed item reject the entire batch before execution", () => BatchBoundsAndLateMalformedItemRejectTheEntireBatchBeforeExecutionCase.Run(Valid, Bytes, Reject));
Add("Adversarial model proposals cannot read secrets, exfiltrate, message, elevate or publish", () => AdversarialModelProposalsCannotReadSecretsExfiltrateMessageElevateOrPublishCase.Run(Bytes));
Add("Model choice cannot bypass response quotas, revoke or stop", () => ModelChoiceCannotBypassResponseQuotasRevokeOrStopCase.Run(Valid, Bytes));
Add("Model source cannot exceed call limit or execute after identity expiry", () => ModelSourceCannotExceedCallLimitOrExecuteAfterIdentityExpiryCase.Run(Valid, Bytes));
Add("Durable gateway keeps model publications unapproved and records actual effects", () => DurableGatewayKeepsModelPublicationsUnapprovedAndRecordsActualEffectsCase.Run());
string Envelope(string text, string status = "completed") => JsonSerializer.Serialize(new { status, output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text } } } } });
Add("Completed Responses envelope parsed and revalidated locally", () => CompletedResponsesEnvelopeParsedAndRevalidatedLocallyCase.Run(Valid, Bytes, Envelope));
Add("Refusal, truncation, tool calls and malformed response envelopes fail closed", () => RefusalTruncationToolCallsAndMalformedResponseEnvelopesFailClosedCase.Run(Valid, Bytes, Envelope));
checks.Add(("Provider request is fixed, tool-free and bounded; prompt contains no credentials", () => ProviderRequestIsFixedToolFreeAndBoundedPromptContainsNoCredentialsCase.RunAsync(Valid, Envelope)));
checks.Add(("Provider redirect, HTTP error and oversized body rejected without retries", () => ProviderRedirectHTTPErrorAndOversizedBodyRejectedWithoutRetriesCase.RunAsync()));
int failures = 0;
foreach ((string? name, Func<Task>? run) in checks)
{
    try
    {
        await run();
        Console.WriteLine("PASS " + name);
    }
    catch (Exception e)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {e}");
    }
}

Console.WriteLine($"{checks.Count - failures}/{checks.Count} offline model checks passed; no provider calls.");
return failures == 0 ? 0 : 1;
void Reject(string text) => CheckAssertions.Throws<ModelOutputException>(() => new ModelProposalSource(Bytes(text)));
