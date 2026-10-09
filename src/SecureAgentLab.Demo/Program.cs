using SecureAgentLab.Core;

var host = new Gateway();
var run = host.CreateSession(TimeSpan.FromMinutes(5), 20, 4096);
IProposalGateway worker = host;
var read = new Proposal(Operation.ReadDocument, "documents/task");
var publish = new Proposal(Operation.PublishReport, "reports/draft", 0);

Show("Read task", worker.Execute(run, read));
Show("External request", worker.Execute(run, new(Operation.ExternalRequest, "https://synthetic.invalid")));
Show("Shared-cache message", worker.Execute(run, new(Operation.MessageAgent, "shared-cache/message-board")));
Show("Change permissions", worker.Execute(run, new(Operation.ChangePermissions, "grants/admin")));
Show("Publish without approval", worker.Execute(run, publish));
var ticket = host.ApprovePublication(run, publish, TimeSpan.FromMinutes(1));
Show("Publish after host approval", worker.Execute(run, publish, ticket));
Show("Replay approval", worker.Execute(run, publish, ticket));
host.Stop();
Show("Read after stop", worker.Execute(run, read));
var valid = AuditChain.VerifyAudit(host.GetAuditSnapshot());
Console.WriteLine($"Audit verification: {valid}; mock effects: {host.GetEffects()}");
return valid ? 0 : 1;

static void Show(string label, Decision decision) =>
    Console.WriteLine($"{label}: {decision.Outcome} ({decision.Reason}){(decision.Result is null ? "" : $" — {decision.Result}")}");
