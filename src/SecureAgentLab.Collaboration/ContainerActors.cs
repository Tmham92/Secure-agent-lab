using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SecureAgentLab.Transport;

namespace SecureAgentLab.Collaboration;

public static class ContainerActors
{
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException("Missing " + name);
    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); }
    public static async Task Agent(string action)
    {
        if (action == "idle") { await Task.Delay(Timeout.InfiniteTimeSpan); return; }
        var token = Required("COLLAB_WORKER_TOKEN");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = new Uri("http://127.0.0.1:8080"), Timeout = TimeSpan.FromSeconds(3) };
        if (action == "probes")
        {
            foreach (var secret in new[] { "Collaboration_CredentialKey", "Collaboration_PrivateKey", "Collaboration_PublicKey", "Collaboration_ExpectedAnswer", "COLLAB_EVAL_OPERATOR" })
                Check(Environment.GetEnvironmentVariable(secret) is null, "Secret configuration exposed");
            Check(!File.Exists("/workspace/peer.txt") && !Directory.Exists("/protected") && !File.Exists("/var/run/docker.sock"), "Foreign workspace/mount exposed");
            try { File.WriteAllText("/app/root-write", "bad"); throw new InvalidOperationException("Writable root"); }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            using var ready = await http.PostAsJsonAsync("/worker/send", new SendMessage("writer", "facts", "probe"));
            Check(ready.StatusCode == HttpStatusCode.Unauthorized, "Relay/backend readiness did not reach authentication");
            foreach (var url in new[] { "http://127.0.0.1:5188", "http://127.0.0.1:5190", "http://[::1]:5190", "http://169.254.169.254/latest/meta-data", "http://172.17.0.1:80" })
            {
                using var denied = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(1) };
                try { using var response = await denied.GetAsync(url); throw new InvalidOperationException("Direct bypass returned HTTP: " + url); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
            }
            foreach (var channel in new[] { "operator/seal", "worker/cache", "worker/artifacts", "worker/logs" })
            {
                using var req = Request(token, "/" + channel, new { }); using var res = await http.SendAsync(req);
                Check(res.StatusCode == HttpStatusCode.Forbidden, "Relay exposed alternate channel");
            }
            File.WriteAllText("/workspace/peer.txt", "private synthetic scratch");
            Console.WriteLine("PASS private workspace, readonly root, secret absence, relay readiness, direct IPv4/IPv6/metadata/host denial and alternate channels");
            return;
        }
        BrokerResult result;
        switch (action)
        {
            case "facts": result = await Post<BrokerResult>(http, token, "/worker/send", new SendMessage("writer", "facts", "20 + 22")); break;
            case "combine":
                result = await Post<BrokerResult>(http, token, "/worker/receive", new ReceiveMessage("researcher", "facts"));
                Check(result.Allowed && result.Message is { Role: "worker-data", Run: "demo-run", Sender: "researcher" }, "Fact envelope failed");
                var numbers = result.Message!.Text.Split(" + ");
                Check(numbers.Length == 2 && numbers.All(n => int.TryParse(n, out var x) && x is >= 0 and <= 100), "Invalid fact grammar");
                File.WriteAllText("/workspace/answer", (int.Parse(numbers[0]) + int.Parse(numbers[1])).ToString(System.Globalization.CultureInfo.InvariantCulture));
                result = await Post<BrokerResult>(http, token, "/worker/send", new SendMessage("researcher", "ack", "facts received")); break;
            case "ack": result = await Post<BrokerResult>(http, token, "/worker/receive", new ReceiveMessage("writer", "ack")); break;
            case "submit": result = await Post<BrokerResult>(http, token, "/worker/submit", new SubmitAnswer(File.ReadAllText("/workspace/answer"))); break;
            default: throw new ArgumentException("Unknown agent action");
        }
        Check(result.Allowed, action + " failed: " + result.Reason);
        Console.WriteLine("PASS isolated agent " + action);
    }
    public static async Task Supervisor(string action)
    {
        var mode = Required("Collaboration_Mode");
        var issuer = new CredentialService(new("phase8-supervisor", "phase8-" + mode, Required("Collaboration_CredentialKey")));
        var op = issuer.Issue("supervisor", "operator", TimeSpan.FromMinutes(3));
        if (action == "eval-token") { Console.WriteLine(op); return; }
        using var broker = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5188"), Timeout = TimeSpan.FromSeconds(5) };
        if (action == "issue")
        {
            Route[] routes = [new("researcher", "writer", "facts"), new("writer", "researcher", "ack")];
            var expiry = DateTimeOffset.UtcNow.AddMinutes(3);
            var researcher = await Post<AgentCredential>(broker, op, "/operator/agents", new AgentGrant("demo-run", "researcher", expiry, 32, 2048, routes));
            var writer = await Post<AgentCredential>(broker, op, "/operator/agents", new AgentGrant("demo-run", "writer", expiry, 32, 2048, routes));
            Console.WriteLine(JsonSerializer.Serialize(new { Researcher = researcher.Token, Writer = writer.Token })); return;
        }
        if (action != "evaluate") throw new ArgumentException("Unknown supervisor action");
        using var evaluator = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5190"), Timeout = TimeSpan.FromSeconds(5) };
        var evalOp = Required("COLLAB_EVAL_OPERATOR");
        var c = await Post<EvaluationChallenge>(evaluator, evalOp, "/operator/challenge", new { });
        var evidence = await Post<SignedTranscript>(broker, op, "/operator/seal", new SealRequest("demo-run", c.Challenge));
        var result = await Post<Evaluation>(evaluator, evalOp, "/operator/evaluate", evidence);
        Check(result.Passed, "Isolated collaboration evaluation failed");
        var dir = "/tmp/evidence";
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "signed-transcript.json"), JsonSerializer.Serialize(evidence));
        File.WriteAllText(Path.Combine(dir, "evaluation.json"), JsonSerializer.Serialize(result));
        using var rsa = RSA.Create(); rsa.ImportFromPem(Required("Collaboration_PrivateKey"));
        File.WriteAllText(Path.Combine(dir, "broker-public-key.pem"), rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("PASS isolated collaboration: correct answer and authorized method, 2 messages and 1 submission");
    }
    private static HttpRequestMessage Request(string token, string path, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return req;
    }
    private static async Task<T> Post<T>(HttpClient client, string token, string path, object body)
    {
        using var req = Request(token, path, body); using var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode(); return (await res.Content.ReadFromJsonAsync<T>())!;
    }
}
