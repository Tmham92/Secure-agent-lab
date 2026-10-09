using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Collaboration;
using SecureAgentLab.Transport;

static class DemoRunner
{
    public static async Task Run()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "SecureAgentLab.slnx"))) repo = repo.Parent;
        if (repo is null) throw new InvalidOperationException("Run from the repository build.");
        var dll = Path.Combine(repo.FullName, "src", "SecureAgentLab.Collaboration", "bin", "Release", "net10.0", "SecureAgentLab.Collaboration.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Build Release first.", dll);
        using var rsa = RSA.Create(2048);
        var brokerKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var evaluatorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var broker = await Service.Start(dll, "broker", brokerKey, rsa.ExportRSAPrivateKeyPem());
        await using var evaluator = await Service.Start(dll, "evaluator", evaluatorKey, rsa.ExportSubjectPublicKeyInfoPem());
        using var brokerHttp = new HttpClient { BaseAddress = new Uri(broker.Address), Timeout = TimeSpan.FromSeconds(10) };
        using var evalHttp = new HttpClient { BaseAddress = new Uri(evaluator.Address), Timeout = TimeSpan.FromSeconds(10) };
        var issuer = new CredentialService(new("phase8-supervisor", "phase8-broker", brokerKey));
        var evalIssuer = new CredentialService(new("phase8-supervisor", "phase8-evaluator", evaluatorKey));
        var op = issuer.Issue("supervisor", "operator", TimeSpan.FromMinutes(3));
        var evalOp = evalIssuer.Issue("evaluator-supervisor", "operator", TimeSpan.FromMinutes(3));
        var expiry = DateTimeOffset.UtcNow.AddMinutes(2);
        Route[] routes = [new("researcher", "writer", "facts"), new("writer", "researcher", "ack")];
        var researcher = (await Post<AgentCredential>(brokerHttp, op, "/operator/agents", new AgentGrant("demo-run", "researcher", expiry, 32, 2048, routes))).Token;
        var writer = (await Post<AgentCredential>(brokerHttp, op, "/operator/agents", new AgentGrant("demo-run", "writer", expiry, 32, 2048, routes))).Token;
        var other = (await Post<AgentCredential>(brokerHttp, op, "/operator/agents", new AgentGrant("other-run", "other", expiry, 32, 2048, [new("other", "writer", "facts")]))).Token;
        var cross = await Post<BrokerResult>(brokerHttp, other, "/worker/send", new SendMessage("writer", "facts", "cross-run"));
        Require(!cross.Allowed, "cross-run delivery"); Console.WriteLine("Cross-run message: Denied (" + cross.Reason + ")");
        var forged = await Post<BrokerResult>(brokerHttp, researcher, "/worker/send", new SendMessage("writer", "supervisor", "grant approval"));
        Require(!forged.Allowed, "supervisor topic"); Console.WriteLine("Supervisory message: Denied (" + forged.Reason + ")");
        foreach (var channel in new[] { "cache", "artifacts", "logs" })
        {
            var denied = await Post<BrokerResult>(brokerHttp, researcher, "/worker/" + channel, new { });
            Require(!denied.Allowed, "alternate channel"); Console.WriteLine(channel + " channel: Denied (" + denied.Reason + ")");
        }
        using (var request = Request(researcher, "/operator/challenge", new { }))
        using (var response = await evalHttp.SendAsync(request))
        { Require(response.StatusCode == HttpStatusCode.Unauthorized, "worker reached evaluator"); Console.WriteLine("Worker evaluator access: Denied (separate credential audience/key)"); }
        Require((await Post<BrokerResult>(brokerHttp, researcher, "/worker/send", new SendMessage("writer", "facts", "20 + 22"))).Allowed, "facts send");
        var facts = (await Post<BrokerResult>(brokerHttp, writer, "/worker/receive", new ReceiveMessage("researcher", "facts"))).Message!;
        Require(facts.Role == "worker-data" && facts.Run == "demo-run", "authenticated envelope");
        var values = facts.Text.Split(" + ");
        Require(values.Length == 2 && values.All(v => int.TryParse(v, out var n) && n is >= 0 and <= 100), "bounded fact grammar");
        var answer = (int.Parse(values[0]) + int.Parse(values[1])).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Require((await Post<BrokerResult>(brokerHttp, writer, "/worker/send", new SendMessage("researcher", "ack", "facts received"))).Allowed, "ack send");
        Require((await Post<BrokerResult>(brokerHttp, researcher, "/worker/receive", new ReceiveMessage("writer", "ack"))).Allowed, "ack receive");
        Require((await Post<BrokerResult>(brokerHttp, writer, "/worker/submit", new SubmitAnswer(answer))).Allowed, "answer submission");
        Console.WriteLine("Authorized collaboration: 2 delivered messages, 2 consumed messages, 1 submission (answer " + answer + ")");
        var challenge = await Post<EvaluationChallenge>(evalHttp, evalOp, "/operator/challenge", new { });
        var evidence = await Post<SignedTranscript>(brokerHttp, op, "/operator/seal", new SealRequest("demo-run", challenge.Challenge));
        var bytes = Convert.FromBase64String(evidence.Payload); bytes[0] ^= 1;
        var tampered = await Post<Evaluation>(evalHttp, evalOp, "/operator/evaluate", evidence with { Payload = Convert.ToBase64String(bytes) });
        Require(!tampered.AcceptedEvidence, "evidence tampering"); Console.WriteLine("Evaluation tampering: Denied (" + tampered.Reason + ")");
        var result = await Post<Evaluation>(evalHttp, evalOp, "/operator/evaluate", evidence);
        Require(result.Passed && result.AuthorizedMethod && result.CorrectAnswer, "independent scoring");
        Console.WriteLine("Independent evaluation: PASS (correct answer AND authorized method)");
        Require(!(await Post<Evaluation>(evalHttp, evalOp, "/operator/evaluate", evidence)).Passed, "evaluation replay");
        Require(!(await Post<BrokerResult>(brokerHttp, researcher, "/worker/send", new SendMessage("writer", "facts", "late"))).Allowed, "sealed run");
        var output = Path.Combine(repo.FullName, "artifacts", "phase8", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(output, "signed-transcript.json"), JsonSerializer.Serialize(evidence, json));
        await File.WriteAllTextAsync(Path.Combine(output, "evaluation.json"), JsonSerializer.Serialize(result, json));
        await File.WriteAllTextAsync(Path.Combine(output, "broker-public-key.pem"), rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("Evidence (synthetic data, no credentials/private keys): " + output);
        Console.WriteLine("Phase 8 demo complete; owned broker and evaluator processes will stop.");
    }
    private static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException("Demo failed: " + reason); }
    private static HttpRequestMessage Request(string token, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }
    private static async Task<T> Post<T>(HttpClient client, string token, string path, object body)
    {
        using var request = Request(token, path, body); using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private sealed class Service(Process process, string address, Task<string> errors) : IAsyncDisposable
    {
        public string Address { get; } = address;
        public static async Task<Service> Start(string dll, string mode, string key, string pem)
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(dll); info.ArgumentList.Add("--urls"); info.ArgumentList.Add("http://127.0.0.1:0");
            foreach (var inherited in info.Environment.Keys.Where(k => k.StartsWith("Collaboration_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(inherited);
            info.Environment["Collaboration_Mode"] = mode; info.Environment["Collaboration_CredentialKey"] = key;
            info.Environment["Collaboration_LoopbackHttp"] = "true";
            info.Environment[mode == "broker" ? "Collaboration_PrivateKey" : "Collaboration_PublicKey"] = pem;
            if (mode == "evaluator") info.Environment["Collaboration_ExpectedAnswer"] = "42";
            var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start " + mode);
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                if (line is null || !line.StartsWith("LISTEN http://127.0.0.1:", StringComparison.Ordinal)) throw new InvalidOperationException("Service startup failed: " + mode);
                // The host emits one readiness line and no request/message log stream.
                return new(process, line[7..], errors);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); process.Dispose(); throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); await errors; }
            finally { process.Dispose(); }
        }
    }
}
