using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SecureAgentLab.Api;
using SecureAgentLab.Transport;
using SecureAgentLab.Core;
using SecureAgentLab.Durable;

internal static class Phase7Checks
{
    public static async Task<int> Run(string root)
    {
        var evidence = Path.Combine(root,"artifacts","phase7",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportPkcs8PrivateKeyPem();
        var tests = new List<(string,Func<Task>)>();
        Fixture Make() => new(evidence,privateKey);
        void Add(string name,Action check) => tests.Add((name,() => { check(); return Task.CompletedTask; }));
        var draft = new Proposal(Operation.PublishReport,"reports/draft",Content:"Reviewed synthetic report.",ExpectedVersion:0);
        Add("Version and exact content are approved; stale parallel approval cannot overwrite",() =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g);
            Equal(new ReportSnapshot(0,null),g.GetReport());
            Equal(Outcome.ApprovalRequired,g.Execute(run,draft).Outcome);
            var ticket=g.ApprovePublication(run,draft,TimeSpan.FromMinutes(1));
            var second=g.ApprovePublication(run,draft with { Content="Other reviewed report." },TimeSpan.FromMinutes(1));
            Equal(0L,g.PreviewPublication(run,draft).ExpectedVersion);
            Equal("approval_mismatch",g.Execute(run,draft with { Content="Injected replacement" },ticket,"a").Reason);
            Equal("approval_mismatch",g.Execute(run,draft with { ExpectedVersion=1 },ticket,"a").Reason);
            Equal("publication_approved",g.Execute(run,draft,ticket,"a").Reason);
            Equal("resource_version_conflict",Open(f).Execute(run,draft with { Content="Other reviewed report." },second,"b").Reason);
            Equal("publication_replayed",Open(f).Execute(run,draft,ticket,"a").Reason);
            Equal(new ReportSnapshot(1,draft.Content),Open(f).GetReport());
            Equal(new MockEffects(0,1),g.GetEffects());
            g.Stop(); Equal(new ReportSnapshot(1,draft.Content),g.GetReport());
        });
        foreach(var point in Enum.GetValues<DurabilityPoint>()) Add($"Versioned write recovers exactly once after {point}",() =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var ticket=g.ApprovePublication(run,draft,TimeSpan.FromMinutes(1));
            var crashing=Open(f,p => { if(p==point) throw new SimulatedCrash(); });
            Throws<SimulatedCrash>(() => crashing.Execute(run,draft,ticket,"crash"));
            g=Open(f); Equal(new ReportSnapshot(1,draft.Content),g.GetReport());
            Equal("publication_replayed",g.Execute(run,draft,ticket,"crash").Reason);
            Equal(new MockEffects(0,1),g.GetEffects());
        });
        Add("Audit outage fails closed before report mutation",() =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var ticket=g.ApprovePublication(run,draft,TimeSpan.FromMinutes(1));
            f.Sink.Offline=true; Equal(Outcome.Denied,g.Execute(run,draft,ticket,"a").Outcome);
            g.SignalRevoke(run); f.Sink.Offline=false;
            Equal(new ReportSnapshot(0,null),g.GetReport()); Equal("identity_revoked",g.Execute(run,draft,ticket,"a").Reason);
        });
        Add("Shared durable budget rejects reset, retry, fanout and survives restart",() =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var path=Path.Combine(f.State,"budget");
            var b=new DurableBudgetStore(path,f.IntegrityKey,f.Clock); b.Create(run,new(f.Clock.GetUtcNow().AddMinutes(1),10,10,10,10));
            Throws<InvalidOperationException>(() => b.Create(run,new(f.Clock.GetUtcNow().AddMinutes(1),10,10,10,10)));
            Equal<string?>(null,b.Reserve(run,new(2,2,2),out var held));
            var other=new DurableBudgetStore(path,f.IntegrityKey,f.Clock);
            Equal("runtime_fanout",other.Reserve(run,new(1,1,1),out _)); b.Release(run,held!);
            Equal("runtime_retry_budget",other.Reserve(run,new(1,1,1,1),out _));
            Equal(8L,other.Snapshot(run).CostMicros); Equal(7,other.Snapshot(run).Attempts);
            f.Clock.Advance(TimeSpan.FromMinutes(1)); Equal("runtime_deadline",other.Reserve(run,new(0,0,0),out _));
        });
        Add("Parallel replicas cannot overspend monetary or token ceilings",() =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var path=Path.Combine(f.State,"budget");
            var b=new DurableBudgetStore(path,f.IntegrityKey,f.Clock); b.Create(run,new(f.Clock.GetUtcNow().AddMinutes(1),100,10,10,10,4));
            var allowed=0;
            Parallel.For(0,50,_ => { var other=new DurableBudgetStore(path,f.IntegrityKey,f.Clock);
                if(other.Reserve(run,new(1,1,1),out var lease) is null) { Interlocked.Increment(ref allowed); other.Release(run,lease!); } });
            Assert(allowed>0 && allowed<=10,"No valid admission or overspent quota"); Equal(10L-allowed,b.Snapshot(run).CostMicros);
            Equal(10L-allowed,b.Snapshot(run).InputTokens); Equal(0,b.Snapshot(run).Active);
            Equal("runtime_cost_budget",b.Reserve(run,new(0,0,11),out _));
            Equal("runtime_token_budget",b.Reserve(run,new(11,0,0),out _));
        });
        Add("Budget tampering and missing state fail closed",() =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var path=Path.Combine(f.State,"budget");
            var b=new DurableBudgetStore(path,f.IntegrityKey,f.Clock); b.Create(run,new(f.Clock.GetUtcNow().AddMinutes(1),10,10,10,10));
            File.WriteAllText(Path.Combine(path,"budgets.json"),"{}");
            Throws<AuthorizationDependencyException>(() => b.Snapshot(run));
            File.Delete(Path.Combine(path,"budgets.json")); Throws<AuthorizationDependencyException>(() => b.Reserve(run,new(1,1,1),out _));
        });
        tests.Add(("Hanging cooperative tool times out, revokes run and creates no effect",async () =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var b=Budget(f,run); var controller=new BoundedRunController(g,b,run,TimeSpan.FromMilliseconds(150));
            var timer=Stopwatch.StartNew(); var result=await controller.RunAsync(new(1,1,1),async ct => { await Task.Delay(Timeout.Infinite,ct); return g.Execute(run,draft); });
            Equal(Outcome.RecoveryRequired,result.Outcome); Assert(timer.Elapsed<TimeSpan.FromSeconds(3),"Containment too slow");
            Equal("tool_timeout",b.Snapshot(run).LastDenial); Equal(new MockEffects(0,0),g.GetEffects());
            Equal("identity_revoked",g.Execute(run,new(Operation.ReadDocument,"documents/task")).Reason);
        }));
        tests.Add(("Replica revocation and global stop cancel active work",async () =>
        {
            foreach(var stop in new[]{false,true})
            {
                var f=Make(); var g=Open(f); var run=f.Run(g); var b=Budget(f,run); var controller=new BoundedRunController(g,b,run,TimeSpan.FromSeconds(2));
                var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var work=controller.RunAsync(new(1,1,1),async ct => { started.SetResult(); await Task.Delay(Timeout.Infinite,ct); return g.Execute(run,draft); });
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                var other=new BoundedRunController(Open(f),new DurableBudgetStore(Path.Combine(f.State,"budget"),f.IntegrityKey,f.Clock),run,TimeSpan.FromSeconds(2));
                if(stop) other.Stop(); else other.Revoke();
                Equal(Outcome.RecoveryRequired,(await work).Outcome); Equal(new MockEffects(0,0),g.GetEffects());
            }
        }));
        tests.Add(("Noncooperative late tool remains quarantined and its later proposal is denied",async () =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var b=Budget(f,run); var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished=new TaskCompletionSource<Decision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var controller=new BoundedRunController(g,b,run,TimeSpan.FromMilliseconds(150));
            Equal(Outcome.RecoveryRequired,(await controller.RunAsync(new(1,1,1),async _ => { await release.Task; var d=g.Execute(run,new(Operation.ReadDocument,"documents/task")); finished.SetResult(d); return d; })).Outcome);
            Equal(1,b.Snapshot(run).Active); release.SetResult(); Equal("identity_revoked",(await finished.Task.WaitAsync(TimeSpan.FromSeconds(3))).Reason);
            Equal(new MockEffects(0,0),g.GetEffects());
        }));
        tests.Add(("Owned hanging worker process is terminated on cancellation",async () =>
        {
            var f=Make(); var g=Open(f); var run=f.Run(g); var b=Budget(f,run);
            var start=new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true };
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("--hang-fixture");
            using var child=Process.Start(start)!;
            try
            {
                Equal("READY",await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                var controller=new BoundedRunController(g,b,run,TimeSpan.FromMilliseconds(150));
                Equal(Outcome.RecoveryRequired,(await controller.RunAsync(new(1,1,1),async ct =>
                {
                    try { await child.WaitForExitAsync(ct); return new(Outcome.Denied,"fixture_exited"); }
                    finally { if(!child.HasExited) child.Kill(entireProcessTree:true); await child.WaitForExitAsync(); }
                })).Outcome);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); Assert(child.HasExited,"Worker survived cancellation");
                Equal(new MockEffects(0,0),g.GetEffects());
            }
            finally { if(!child.HasExited) { child.Kill(entireProcessTree:true); await child.WaitForExitAsync(); } }
        }));
        tests.Add(("Authenticated HTTP versioned write uses trusted runtime admission and blocks worker report access",async () =>
        {
            var f=Make(); var g=Open(f);
            var settings=new CredentialSettings("phase7","gateway",Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var credentials=new CredentialService(settings,f.Clock);
            await using var app=LabApi.Build(["--urls","http://127.0.0.1:0","--Logging:LogLevel:Default","Error",
                "--Lab:EnableRuntimeLimits","true","--Lab:StateDirectory",f.State],settings,f.Clock,g,true);
            await app.StartAsync();
            using var client=new HttpClient { BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            client.DefaultRequestHeaders.Authorization=new("Bearer",credentials.Issue("reviewer","operator",TimeSpan.FromMinutes(5)));
            using var issued=await client.PostAsJsonAsync("/operator/runs",new IssueRunRequest()); issued.EnsureSuccessStatusCode();
            var run=(await issued.Content.ReadFromJsonAsync<IssuedRun>())!;
            using var approved=await client.PostAsJsonAsync($"/operator/runs/{run.RunId}/approvals",new ApprovalRequest(draft)); approved.EnsureSuccessStatusCode();
            var ticket=(await approved.Content.ReadFromJsonAsync<IssuedApproval>())!.Ticket;
            client.DefaultRequestHeaders.Authorization=new("Bearer",run.WorkerCredential);
            using var hidden=await client.GetAsync("/operator/report"); Equal(HttpStatusCode.Forbidden,hidden.StatusCode);
            using var written=await client.PostAsJsonAsync("/worker/proposals",new ExecuteRequest(draft,ticket,"http")); written.EnsureSuccessStatusCode();
            Equal("publication_approved",(await written.Content.ReadFromJsonAsync<Decision>())!.Reason);
            Equal(new ReportSnapshot(1,draft.Content),g.GetReport());
            var key=HMACSHA256.HashData(settings.Validate(),System.Text.Encoding.UTF8.GetBytes("runtime-budget-v1"));
            Equal(999L,new DurableBudgetStore(Path.Combine(f.State,"runtime-budgets"),key,f.Clock).Snapshot(run.RunId).InputTokens);
            await app.StopAsync();
        }));
        var failures=0;
        var results=new List<object>();
        foreach(var (name,check) in tests)
        {
            var elapsed=Stopwatch.StartNew(); var passed=false;
            try { await check(); passed=true; Console.WriteLine("PASS "+name); }
            catch(Exception e) { failures++; Console.Error.WriteLine("FAIL "+name+": "+e); }
            results.Add(new { Scenario=name,Passed=passed,ElapsedMilliseconds=elapsed.ElapsedMilliseconds });
        }
        File.WriteAllText(Path.Combine(evidence,"evidence.json"),JsonSerializer.Serialize(new { Phase=7,Checks=tests.Count,Passed=tests.Count-failures,Utc=DateTimeOffset.UtcNow,Results=results },new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine($"{tests.Count-failures}/{tests.Count} phase 7 checks passed. Evidence: {evidence}");
        return failures==0?0:1;
    }
    private static DurableGateway Open(Fixture f,Action<DurabilityPoint>? crash=null) => new(f.State,f.Sink,((AuditCollectorStore)f.Sink.Inner).PublicKey,f.Stream,f.IntegrityKey,clock:f.Clock,crash:crash,enableReportWrites:true);
    private static DurableBudgetStore Budget(Fixture f,string run) { var b=new DurableBudgetStore(Path.Combine(f.State,"budget"),f.IntegrityKey,f.Clock); b.Create(run,new(f.Clock.GetUtcNow().AddMinutes(1),30,100,100,100)); return b; }
    private static void Assert(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected,T actual) => Assert(EqualityComparer<T>.Default.Equals(expected,actual),$"Expected {expected}, got {actual}");
    private static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new InvalidOperationException("Expected "+typeof(T).Name); }
}
