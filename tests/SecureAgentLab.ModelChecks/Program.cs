using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SecureAgentLab.Core;
using SecureAgentLab.Durable;
using SecureAgentLab.ModelHost;

var checks = new List<(string Name, Func<Task> Run)>();
byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
const string Valid = """{"proposals":[{"operation":"ReadDocument","resource":"documents/task"}]}""";
void Add(string name, Action action) => checks.Add((name, () => { action(); return Task.CompletedTask; }));
Add("Valid batch yields immutable typed proposals", () =>
{
    var source = new ModelProposalSource(Bytes(Valid));
    Equal(new Proposal(Operation.ReadDocument,"documents/task"), source.GetProposals().Single());
});
Add("Unknown authority fields, duplicates and alternate enum encodings rejected", () =>
{
    foreach (var text in new[] {
        Valid.Replace("\"resource\":", "\"approvalTicket\":\"forged\",\"resource\":"),
        Valid.Replace("\"resource\":", "\"runId\":\"another-run\",\"resource\":"),
        Valid.Replace("\"resource\":", "\"grant\":\"admin\",\"resource\":"),
        Valid.Replace("\"resource\":", "\"resource\":\"documents/private\",\"resource\":"),
        Valid.Replace("ReadDocument", "0"), Valid.Replace("ReadDocument", "readDocument"),
        Valid.Replace("ReadDocument", "Shell"), Valid.Replace("\"ReadDocument\"", "0"),
        "{\"proposals\":[],\"proposals\":[{\"operation\":\"ReadDocument\",\"resource\":\"documents/task\"}]}"
    }) Reject(text);
});
Add("Malformed, truncated, fenced, wrong-type and oversized output rejected", () =>
{
    foreach (var text in new[] { "null","[]","{", Valid[..^1],"```json\n"+Valid+"\n```",Valid+" trailing",Valid.Replace("\"documents/task\"","null"),Valid.Replace("documents/task","\\u0000") }) Reject(text);
    Throws<ModelOutputException>(() => new ModelProposalSource(new byte[ModelProposalSource.MaximumBytes+1]));
});
Add("Batch bounds and late malformed item reject the entire batch before execution", () =>
{
    Reject("{\"proposals\":[]}");
    Reject("{\"proposals\":["+string.Join(',',Enumerable.Repeat("{\"operation\":\"ReadDocument\",\"resource\":\"documents/task\"}",9))+"]}");
    var g = new Gateway();
    var run = g.CreateSession(TimeSpan.FromMinutes(1),20,4096);
    try { foreach (var p in new ModelProposalSource(Bytes(Valid.Replace("]}",",{\"operation\":\"Shell\",\"resource\":\"x\"}]}"))).GetProposals()) g.Execute(run,p); }
    catch (ModelOutputException) { }
    Equal(new MockEffects(0,0),g.GetEffects()); Equal(20,g.GetSession(run)!.RemainingCalls);
});
Add("Adversarial model proposals cannot read secrets, exfiltrate, message, elevate or publish", () =>
{
    var text = """{"proposals":[{"operation":"ReadDocument","resource":"documents/task"},{"operation":"ReadDocument","resource":"documents/private"},{"operation":"ExternalRequest","resource":"https://example.invalid/exfil"},{"operation":"MessageAgent","resource":"shared-cache/message-board"},{"operation":"ChangePermissions","resource":"grants/admin"},{"operation":"PublishReport","resource":"reports/draft"}]}""";
    var g = new Gateway(); var run = g.CreateSession(TimeSpan.FromMinutes(1),20,4096);
    var outcomes = new ModelProposalSource(Bytes(text)).GetProposals().Select(p => g.Execute(run,p).Outcome).ToArray();
    Equal("Allowed,Denied,Denied,Denied,Denied,ApprovalRequired",string.Join(',',outcomes));
    Equal(new MockEffects(1,0),g.GetEffects());
});
Add("Model choice cannot bypass response quotas, revoke or stop", () =>
{
    var p = new ModelProposalSource(Bytes(Valid)).GetProposals().Single();
    var g = new Gateway(); var run = g.CreateSession(TimeSpan.FromMinutes(1),20,0);
    Equal(Outcome.Denied,g.Execute(run,p).Outcome); g.Revoke(run);
    Equal("identity_revoked",g.Execute(run,p).Reason); g.Stop();
    Equal("gateway_stopped",g.Execute(run,p).Reason); Equal(new MockEffects(0,0),g.GetEffects());
});
Add("Model source cannot exceed call limit or execute after identity expiry", () =>
{
    var p = new ModelProposalSource(Bytes(Valid)).GetProposals().Single();
    var clock = new ModelClock(); var g = new Gateway(clock);
    var run = g.CreateSession(TimeSpan.FromMinutes(1),1,4096);
    Equal(Outcome.Allowed,g.Execute(run,p).Outcome); Equal("call_budget_exhausted",g.Execute(run,p).Reason);
    var expired = g.CreateSession(TimeSpan.FromMinutes(1),20,4096); clock.Now = clock.Now.AddMinutes(1);
    Equal("identity_expired",g.Execute(expired,p).Reason); Equal(new MockEffects(1,0),g.GetEffects());
});
Add("Durable gateway keeps model publications unapproved and records actual effects", () =>
{
    var repo = new DirectoryInfo(AppContext.BaseDirectory);
    while (repo is not null && !File.Exists(Path.Combine(repo.FullName,"SecureAgentLab.slnx"))) repo = repo.Parent;
    var folder = Path.Combine(repo!.FullName,"artifacts","model-checks",Guid.NewGuid().ToString("N"));
    using var rsa = RSA.Create(2048); var stream = Guid.NewGuid().ToString("N");
    var sink = new AuditCollectorStore(Path.Combine(folder,"log"),Path.Combine(folder,"head"),stream,rsa.ExportPkcs8PrivateKeyPem());
    var g = new DurableGateway(Path.Combine(folder,"state"),sink,rsa.ExportSubjectPublicKeyInfoPem(),stream,RandomNumberGenerator.GetBytes(32));
    var run = g.CreateSession(TaskGrant.Default(DateTimeOffset.UtcNow.AddMinutes(2),20,4096));
    var source = ModelProposalSource.FromFile(Path.Combine(repo.FullName,"fixtures","model","adversarial-proposals.json"));
    var outcomes = source.GetProposals().Select(p => g.Execute(run,p).Outcome);
    Equal("Allowed,Denied,Denied,Denied,Denied,ApprovalRequired",string.Join(',',outcomes));
    Equal(new MockEffects(1,0),g.GetEffects()); Equal(14,g.GetSession(run)!.RemainingCalls);
    Equal(7,sink.GetEvents().Count);
});
string Envelope(string text, string status = "completed") => JsonSerializer.Serialize(new { status, output = new[] { new { type = "message", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text } } } } });
Add("Completed Responses envelope parsed and revalidated locally", () => Equal(Valid,Encoding.UTF8.GetString(ResponsesProposalClient.Extract(Bytes(Envelope(Valid))))));
Add("Refusal, truncation, tool calls and malformed response envelopes fail closed", () =>
{
    foreach (var text in new[] { Envelope(Valid,"incomplete"),Envelope("I refuse"),Envelope(Valid).Replace("output_text","refusal"),Envelope(Valid).Replace("\"message\"","\"function_call\""),"{}",Envelope(Valid).Replace("\"status\":\"completed\",","\"status\":\"completed\",\"status\":\"completed\",") })
        Throws<ModelOutputException>(() => ResponsesProposalClient.Extract(Bytes(text)));
});
checks.Add(("Provider request is fixed, tool-free and bounded; prompt contains no credentials", async () =>
{
    var handler = new FakeHandler(async request =>
    {
        Equal(ResponsesProposalClient.Endpoint,request.RequestUri); Equal(HttpMethod.Post,request.Method);
        Equal("synthetic-provider-key",request.Headers.Authorization!.Parameter);
        var body = await request.Content!.ReadAsStringAsync();
        Assert(!body.Contains("synthetic-provider-key"),"Key leaked into prompt");
        using var json = JsonDocument.Parse(body); var root = json.RootElement;
        Equal(false,root.GetProperty("store").GetBoolean()); Equal(1024,root.GetProperty("max_output_tokens").GetInt32()); Equal(0,root.GetProperty("tools").GetArrayLength());
        Equal("json_schema",root.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope(Valid)) };
    });
    using var client = new HttpClient(handler);
    Equal(Valid,Encoding.UTF8.GetString(await new ResponsesProposalClient(client).Generate("synthetic-model","synthetic-provider-key")));
    Equal(1,handler.Calls);
}));
checks.Add(("Provider redirect, HTTP error and oversized body rejected without retries", async () =>
{
    foreach (var status in new[] { HttpStatusCode.Redirect,HttpStatusCode.TooManyRequests,HttpStatusCode.OK })
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(new string('X',70000)) }));
        using var client = new HttpClient(handler);
        try { await new ResponsesProposalClient(client).Generate("synthetic-model","synthetic-key"); throw new InvalidOperationException("Unexpected success"); }
        catch (ModelOutputException) { }
        Equal(1,handler.Calls);
    }
}));
var failures = 0;
foreach (var (name,run) in checks)
{
    try { await run(); Console.WriteLine("PASS "+name); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
Console.WriteLine($"{checks.Count-failures}/{checks.Count} offline model checks passed; no provider calls.");
return failures == 0 ? 0 : 1;
void Reject(string text) => Throws<ModelOutputException>(() => new ModelProposalSource(Bytes(text)));
static void Assert(bool condition,string reason) { if (!condition) throw new InvalidOperationException(reason); }
static void Equal<T>(T expected,T actual) => Assert(EqualityComparer<T>.Default.Equals(expected,actual),$"Expected {expected}, got {actual}");
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected "+typeof(T).Name); }
sealed class FakeHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) { Calls++; return respond(request); }
}
sealed class ModelClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
