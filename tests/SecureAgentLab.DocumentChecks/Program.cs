using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.Core;
using SecureAgentLab.Durable;
using SecureAgentLab.Transport;

var repo = new DirectoryInfo(AppContext.BaseDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "SecureAgentLab.slnx"))) repo = repo.Parent;
var basePath = Path.Combine(repo!.FullName, "artifacts", "document-checks", Guid.NewGuid().ToString("N"));
var checks = new List<(string Name, Func<Task> Run)>();
var read = new Proposal(Operation.ReadDocument, "documents/task", 0);
string Fixture()
{
    var root = Path.Combine(basePath, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "task"));
    Directory.CreateDirectory(Path.Combine(root, "reference"));
    File.WriteAllText(Path.Combine(root, "task", "task.txt"), Gateway.TaskDocument, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(root, "reference", "reference.txt"), SyntheticDocuments.Reference, new UTF8Encoding(false));
    return root;
}
FileDocumentReader Reader(string root, int max = 4096) => new(root, SyntheticDocuments.Catalog, max);
Gateway GatewayFor(string root) => new(new DefaultPolicy(), new DocumentExecutor(Reader(root)));
string Grant(Gateway g, string id, long bytes = 4096) => g.CreateSession(new TaskGrant("synthetic-v1", DateTimeOffset.UtcNow.AddMinutes(3), 20, bytes, [new(Operation.ReadDocument, id)]));
void Add(string name, Action run) => checks.Add((name, () => { run(); return Task.CompletedTask; }));
Add("Exact file read and measured multibyte response quota", () =>
{
    var root = Fixture();
    var g = GatewayFor(root); var bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
    var run = Grant(g, "documents/task", bytes);
    Equal(Gateway.TaskDocument, g.Execute(run, read).Result);
    Equal(0L, g.GetSession(run)!.RemainingResponseBytes);
    Equal("response_budget_exhausted", g.Execute(run, read).Reason);
    Equal(new MockEffects(1,0), g.GetEffects());
});
Add("Cross-task grants cannot read the other catalog document", () =>
{
    var g = GatewayFor(Fixture()); var a = Grant(g, "documents/task"); var b = Grant(g, "documents/reference");
    Equal(Outcome.Denied, g.Execute(a, new(Operation.ReadDocument,"documents/reference")).Outcome);
    Equal(Outcome.Denied, g.Execute(b, read).Outcome);
    Equal(SyntheticDocuments.Reference, g.Execute(b, new(Operation.ReadDocument,"documents/reference")).Result);
    Equal(new MockEffects(1,0), g.GetEffects());
});
Add("Traversal, absolute paths, URLs, ADS and encoded aliases are rejected", () =>
{
    var root = Fixture(); var g = GatewayFor(root); var run = Grant(g,"documents/task");
    foreach (var id in new[] { "../task/task.txt", "documents/../private", "/etc/passwd", "C:\\secret.txt", "documents/task:secret", "documents%2ftask", "documents/task/", "DOCUMENTS/task", "http://127.0.0.1/task" })
    {
        Equal(Outcome.Denied, g.Execute(run, new(Operation.ReadDocument,id)).Outcome);
        Equal(Outcome.Denied, Reader(root).Read(id,4096).Outcome);
    }
    Equal(new MockEffects(0,0), g.GetEffects());
});
Add("Host catalog rejects traversal, platform separators, aliases and invalid pins", () =>
{
    var root = Fixture();
    foreach (var path in new[] { "../task.txt", "/task.txt", "task/../task.txt", "task\\task.txt", "task//task.txt", "task/task.txt:stream", "task/%74ask.txt" })
        Throws<ArgumentException>(() => new FileDocumentReader(root,[new("documents/task",path,new string('A',64))]));
    Throws<ArgumentException>(() => new FileDocumentReader(root,[new("documents/private","task/task.txt",new string('A',64))]));
    Throws<ArgumentException>(() => new FileDocumentReader(root,[new("documents/task","task/task.txt","invalid")]));
});
Add("Oversized documents never return partial content or charge response bytes", () =>
{
    var root = Fixture(); File.WriteAllText(Path.Combine(root,"task","task.txt"), new string('X',70000));
    var g = GatewayFor(root); var run = Grant(g,"documents/task",65536);
    var result = g.Execute(run,read); Equal("document_too_large",result.Reason); Equal<string?>(null,result.Result);
    Equal(65536L,g.GetSession(run)!.RemainingResponseBytes); Equal(0,g.GetEffects().DocumentReads);
});
Add("Zero estimate cannot bypass actual byte limit", () =>
{
    var g = GatewayFor(Fixture()); var run = Grant(g,"documents/task",Encoding.UTF8.GetByteCount(Gateway.TaskDocument)-1);
    Equal("response_budget_exhausted",g.Execute(run,read).Reason); Equal(0,g.GetEffects().DocumentReads);
});
Add("Changed file and malformed UTF-8 fail closed", () =>
{
    var root = Fixture(); var path = Path.Combine(root,"task","task.txt");
    File.WriteAllText(path,"synthetic target credential changed by host");
    Equal("document_version_mismatch",Reader(root).Read("documents/task",4096).Reason);
    byte[] bad = [0xc3,0x28]; File.WriteAllBytes(path,bad);
    var reader = new FileDocumentReader(root,[new("documents/task","task/task.txt",SyntheticDocuments.Hash(bad))]);
    Equal("document_unavailable",reader.Read("documents/task",4096).Reason);
});
Add("Missing executor file denies without paths, secret bytes or read effects", () =>
{
    var root = Fixture(); File.Delete(Path.Combine(root,"task","task.txt"));
    var g = GatewayFor(root); var run = Grant(g,"documents/task"); var result = g.Execute(run,read);
    Equal(new Decision(Outcome.Denied,"document_unavailable"),result);
    Equal(19,g.GetSession(run)!.RemainingCalls); Equal(0,g.GetEffects().DocumentReads);
    Assert(!JsonSerializer.Serialize(g.GetAuditSnapshot()).Contains(root),"Host path leaked");
});
Add("Hostile document text remains data and cannot enable operations", () =>
{
    var g = GatewayFor(Fixture()); var run = Grant(g,"documents/reference");
    Equal(SyntheticDocuments.Reference,g.Execute(run,new(Operation.ReadDocument,"documents/reference")).Result);
    foreach (var p in new[] { new Proposal(Operation.ReadDocument,"documents/private"), new(Operation.ExternalRequest,"https://example.invalid"), new(Operation.ChangePermissions,"grants/admin"), new(Operation.PublishReport,"reports/draft") })
        Equal(Outcome.Denied,g.Execute(run,p).Outcome);
    Equal(new MockEffects(1,0),g.GetEffects());
});
Add("Read proposal cannot supply alternate content", () =>
{
    var g = GatewayFor(Fixture()); var run = Grant(g,"documents/task");
    Equal(Outcome.Denied,g.Execute(run,read with { Content = "forged content" }).Outcome);
    Equal(0,g.GetEffects().DocumentReads);
});
Add("Executor-only credential canary is outside catalog and never returned", () =>
{
    var root = Fixture(); var secret = "synthetic-executor-only-credential";
    File.WriteAllText(Path.Combine(root,"credential.txt"),secret);
    var g = GatewayFor(root); var run = Grant(g,"documents/task");
    foreach (var id in new[] { "documents/private", "../credential.txt", Path.Combine(root,"credential.txt") })
        Equal<string?>(null,g.Execute(run,new(Operation.ReadDocument,id)).Result);
    Assert(!g.Execute(run,read).Result!.Contains(secret),"Credential canary leaked");
    Assert(!JsonSerializer.Serialize(g.GetAuditSnapshot()).Contains(secret),"Credential canary leaked into audit");
});
Add("Directory junction/symlink and root alias are rejected", () =>
{
    var root = Fixture(); var outside = Fixture(); File.Delete(Path.Combine(root,"task","task.txt")); Directory.Delete(Path.Combine(root,"task"));
    LinkDirectory(Path.Combine(root,"task"),Path.Combine(outside,"task"));
    Equal("document_unavailable",Reader(root).Read("documents/task",4096).Reason);
    var alias = root + "-alias"; LinkDirectory(alias,outside);
    Equal("document_unavailable",Reader(alias).Read("documents/task",4096).Reason);
});
if (OperatingSystem.IsLinux())
{
    Add("Final symlink is rejected even when destination has correct pinned bytes", () =>
    {
        var root = Fixture(); var outside = Fixture(); var path = Path.Combine(root,"task","task.txt"); File.Delete(path);
        File.CreateSymbolicLink(path,Path.Combine(outside,"task","task.txt"));
        Equal("document_unavailable",Reader(root).Read("documents/task",4096).Reason);
    });
    Add("FIFO is rejected without blocking", () =>
    {
        var root = Fixture(); var path = Path.Combine(root,"task","task.txt"); File.Delete(path);
        using var process = Process.Start(new ProcessStartInfo("mkfifo") { ArgumentList = { path } })!;
        process.WaitForExit(); Equal(0,process.ExitCode);
        Equal("document_unavailable",Reader(root).Read("documents/task",4096).Reason);
    });
}
Add("Durable reader preserves measured quotas across restart and redacts document bodies", () =>
{
    var root = Fixture(); using var rsa = RSA.Create(2048); var stream = Guid.NewGuid().ToString("N");
    var sink = new AuditCollectorStore(Path.Combine(root,"audit"),Path.Combine(root,"head"),stream,rsa.ExportPkcs8PrivateKeyPem());
    var key = RandomNumberGenerator.GetBytes(32); var state = Path.Combine(root,"state");
    DurableGateway Open() => new(state,sink,rsa.ExportSubjectPublicKeyInfoPem(),stream,key,documents:Reader(root));
    var g = Open(); var bytes = Encoding.UTF8.GetByteCount(Gateway.TaskDocument);
    var run = g.CreateSession(TaskGrant.Default(DateTimeOffset.UtcNow.AddMinutes(3),20,bytes));
    Equal(Gateway.TaskDocument,g.Execute(run,read).Result); g = Open();
    Equal(0L,g.GetSession(run)!.RemainingResponseBytes); Equal("response_budget_exhausted",g.Execute(run,read).Reason);
    Equal(new MockEffects(1,0),g.GetEffects());
    var events = sink.GetEvents();
    Equal(SyntheticDocuments.Catalog[0].Sha256,events.Single(e => e.Entry.Reason == "scoped_read").Entry.ContentHash);
    var json = JsonSerializer.Serialize(events); Assert(!json.Contains("Café") && !json.Contains(root),"Document body or path leaked into audit");
});
checks.Add(("Authenticated HTTP uses file adapter and derives scope from identity", async () =>
{
    var root = Fixture(); var gateway = GatewayFor(root);
    var settings = new CredentialSettings("document-host","document-gateway",Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    await using var app = LabApi.Build(["--urls","http://127.0.0.1:0","--Logging:LogLevel:Default","Warning"],settings,gateway:gateway,allowLoopbackHttp:true);
    await app.StartAsync();
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new(address) };
        using var unauth = await client.PostAsJsonAsync("/worker/proposals",new ExecuteRequest(read)); Equal(HttpStatusCode.Unauthorized,unauth.StatusCode);
        var run = Grant(gateway,"documents/task");
        client.DefaultRequestHeaders.Authorization = new("Bearer",new CredentialService(settings).Issue(run,"worker",TimeSpan.FromMinutes(2),gateway.GetGrant(run)!.Fingerprint));
        using var allowed = await client.PostAsJsonAsync("/worker/proposals",new ExecuteRequest(read));
        Equal(Gateway.TaskDocument,(await allowed.Content.ReadFromJsonAsync<Decision>())!.Result);
        using var other = await client.PostAsJsonAsync("/worker/proposals",new ExecuteRequest(new(Operation.ReadDocument,"documents/reference")));
        Equal(Outcome.Denied,(await other.Content.ReadFromJsonAsync<Decision>())!.Outcome);
        Equal(new MockEffects(1,0),gateway.GetEffects());
    }
    finally { await app.StopAsync(); }
}));
var failures = 0;
foreach (var (name,run) in checks)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e}"); }
}
Console.WriteLine($"{checks.Count-failures}/{checks.Count} document checks passed. Linux adds final-symlink and FIFO checks.");
return failures == 0 ? 0 : 1;

static void Assert(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
static void Equal<T>(T expected,T actual) => Assert(EqualityComparer<T>.Default.Equals(expected,actual),$"Expected {expected}, got {actual}");
static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
static void LinkDirectory(string path,string target)
{
    if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path,target); return; }
    // Junction fixtures do not require Windows symbolic-link privilege. Paths are generated by this harness.
    var command = $"New-Item -ItemType Junction -Path '{path.Replace("'","''")}' -Target '{target.Replace("'","''")}' -ErrorAction Stop | Out-Null";
    using var process = Process.Start(new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, UseShellExecute = false,
        ArgumentList = { "-NoProfile","-NonInteractive","-Command",command } })!;
    process.WaitForExit(); Equal(0,process.ExitCode);
}
